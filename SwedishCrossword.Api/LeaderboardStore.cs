using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace SwedishCrossword.Api;

/// <summary>
/// Leaderboard, history, user alias, and friends storage.
/// Uses Azure SQL in production (when ConnectionStrings:Leaderboard is set)
/// and falls back to SQLite for local development and testing.
/// </summary>
sealed class LeaderboardStore : IDisposable
{
    public static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private static readonly DateOnly HistoryCutoffDate = new(2026, 4, 14);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly string _connectionString;
    private readonly string _dataDir;
    private readonly ILogger<LeaderboardStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly bool _useSqlServer;

    public LeaderboardStore(IConfiguration config, ILogger<LeaderboardStore> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;

        var sqlConnStr = config.GetConnectionString("Leaderboard");
        _useSqlServer = !string.IsNullOrWhiteSpace(sqlConnStr);

        if (_useSqlServer)
        {
            _connectionString = sqlConnStr!;
            _dataDir = string.Empty;
            _logger.LogInformation("Using Azure SQL for leaderboard storage");
        }
        else
        {
            var path = config["Storage:LeaderboardPath"];
            _dataDir = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(AppContext.BaseDirectory, "leaderboard")
                : path;
            Directory.CreateDirectory(_dataDir);

            var dbPath = Path.Combine(_dataDir, "leaderboard.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString();
            _logger.LogInformation("Using SQLite for leaderboard storage at {Path}", dbPath);
        }

        InitialiseDatabase();

        if (!_useSqlServer)
            MigrateFromJsonFiles();
    }

    public static string SanitiseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sanitised = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return sanitised[..Math.Min(sanitised.Length, 30)];
    }

    // GET /leaderboard
    public async Task<string> GetCurrentAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT leaderboard_key, name, time, timestamp, puzzle_hash, hints_used, word_hints_used, user_id
            FROM scores
            ORDER BY leaderboard_key, time
            """;

        var allScores = new Dictionary<string, List<ScoreRecord>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var hasUserId = !reader.IsDBNull(7);
            var record = new ScoreRecord(
                Name: reader.GetString(1),
                Time: reader.GetDouble(2),
                Timestamp: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                PuzzleHash: reader.IsDBNull(4) ? null : reader.GetString(4),
                HintsUsed: reader.GetInt32(5),
                WordHintsUsed: reader.GetInt32(6),
                UserId: hasUserId ? "verified" : null
            );

            if (!allScores.TryGetValue(key, out var list))
            {
                list = [];
                allScores[key] = list;
            }
            list.Add(record);
        }

        return JsonSerializer.Serialize(new { scores = allScores }, JsonOptions);
    }

    // POST /api/scores
    public async Task<List<ScoreRecord>> AppendScoreAsync(string leaderboardKey, ScoreRecord entry)
    {
        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Deduplicate
        await using (var dedup = conn.CreateCommand())
        {
            dedup.Transaction = tx;
            dedup.CommandText = _useSqlServer
                ? """
                  SELECT COUNT(1) FROM scores
                  WHERE leaderboard_key = @key AND name = @name
                    AND ABS(time - @time) < 0.001
                    AND ((timestamp IS NULL AND @timestamp IS NULL) OR timestamp = @timestamp)
                  """
                : """
                  SELECT COUNT(1) FROM scores
                  WHERE leaderboard_key = @key AND name = @name
                    AND ABS(time - @time) < 0.001
                    AND timestamp IS @timestamp
                  """;
            AddParam(dedup, "@key", leaderboardKey);
            AddParam(dedup, "@name", entry.Name);
            AddParam(dedup, "@time", entry.Time);
            AddParam(dedup, "@timestamp", (object?)entry.Timestamp ?? DBNull.Value);

            var count = Convert.ToInt64(await dedup.ExecuteScalarAsync());
            if (count > 0)
            {
                await tx.CommitAsync();
                return await GetScoresForKeyAsync(conn, null, leaderboardKey);
            }
        }

        // Insert
        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO scores (leaderboard_key, name, time, timestamp, puzzle_hash, hints_used, word_hints_used, user_id)
                VALUES (@key, @name, @time, @timestamp, @hash, @hints, @wordHints, @userId)
                """;
            AddParam(insert, "@key", leaderboardKey);
            AddParam(insert, "@name", entry.Name);
            AddParam(insert, "@time", entry.Time);
            AddParam(insert, "@timestamp", (object?)entry.Timestamp ?? DBNull.Value);
            AddParam(insert, "@hash", (object?)entry.PuzzleHash ?? DBNull.Value);
            AddParam(insert, "@hints", entry.HintsUsed);
            AddParam(insert, "@wordHints", entry.WordHintsUsed);
            AddParam(insert, "@userId", (object?)entry.UserId ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        // Keep top 10 per key
        await using (var trim = conn.CreateCommand())
        {
            trim.Transaction = tx;
            trim.CommandText = _useSqlServer
                ? """
                  DELETE FROM scores
                  WHERE leaderboard_key = @key
                    AND id NOT IN (
                      SELECT TOP 10 id FROM scores
                      WHERE leaderboard_key = @key
                      ORDER BY time
                    )
                  """
                : """
                  DELETE FROM scores
                  WHERE leaderboard_key = @key
                    AND rowid NOT IN (
                      SELECT rowid FROM scores
                      WHERE leaderboard_key = @key
                      ORDER BY time LIMIT 10
                    )
                  """;
            AddParam(trim, "@key", leaderboardKey);
            await trim.ExecuteNonQueryAsync();
        }

        // Prune entries older than 7 days
        var cutoff = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(-7);
        if (cutoff < HistoryCutoffDate) cutoff = HistoryCutoffDate;
        await using (var prune = conn.CreateCommand())
        {
            prune.Transaction = tx;
            prune.CommandText = _useSqlServer
                ? "DELETE FROM scores WHERE SUBSTRING(leaderboard_key, 1, 10) < @cutoff"
                : "DELETE FROM scores WHERE SUBSTR(leaderboard_key, 1, 10) < @cutoff";
            AddParam(prune, "@cutoff", cutoff.ToString("yyyy-MM-dd"));
            await prune.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return await GetScoresForKeyAsync(conn, null, leaderboardKey);
    }

    // POST /leaderboard/history
    public async Task AppendHistoryAsync(string date, HistoryRecord record)
    {
        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        // Deduplicate
        await using (var dedup = conn.CreateCommand())
        {
            dedup.Transaction = tx;
            dedup.CommandText = _useSqlServer
                ? """
                  SELECT COUNT(1) FROM history
                  WHERE date = @date AND name = @name
                    AND ABS(time - @time) < 0.001
                    AND ((timestamp IS NULL AND @timestamp IS NULL) OR timestamp = @timestamp)
                  """
                : """
                  SELECT COUNT(1) FROM history
                  WHERE date = @date AND name = @name
                    AND ABS(time - @time) < 0.001
                    AND timestamp IS @timestamp
                  """;
            AddParam(dedup, "@date", date);
            AddParam(dedup, "@name", record.Name);
            AddParam(dedup, "@time", record.Time);
            AddParam(dedup, "@timestamp", (object?)record.Timestamp ?? DBNull.Value);

            var count = Convert.ToInt64(await dedup.ExecuteScalarAsync());
            if (count > 0)
            {
                await tx.CommitAsync();
                return;
            }
        }

        // Insert
        await using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO history (date, name, time, timestamp, puzzle_hash, puzzle_size, hints_used, word_hints_used, user_id)
                VALUES (@date, @name, @time, @timestamp, @hash, @size, @hints, @wordHints, @userId)
                """;
            AddParam(insert, "@date", date);
            AddParam(insert, "@name", record.Name);
            AddParam(insert, "@time", record.Time);
            AddParam(insert, "@timestamp", (object?)record.Timestamp ?? DBNull.Value);
            AddParam(insert, "@hash", (object?)record.PuzzleHash ?? DBNull.Value);
            AddParam(insert, "@size", (object?)record.PuzzleSize ?? DBNull.Value);
            AddParam(insert, "@hints", record.HintsUsed);
            AddParam(insert, "@wordHints", record.WordHintsUsed);
            AddParam(insert, "@userId", (object?)record.UserId ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        // Keep top 10 per puzzle_hash for this date
        await using (var trimPerHash = conn.CreateCommand())
        {
            trimPerHash.Transaction = tx;
            trimPerHash.CommandText = _useSqlServer
                ? """
                  DELETE FROM history
                  WHERE date = @date AND id NOT IN (
                      SELECT id FROM (
                          SELECT id,
                                 ROW_NUMBER() OVER (
                                     PARTITION BY COALESCE(puzzle_hash, '_default')
                                     ORDER BY time
                                 ) AS rn
                          FROM history WHERE date = @date
                      ) sub WHERE rn <= 10
                  )
                  """
                : """
                  DELETE FROM history
                  WHERE date = @date AND rowid NOT IN (
                      SELECT rowid FROM (
                          SELECT rowid,
                                 ROW_NUMBER() OVER (
                                     PARTITION BY COALESCE(puzzle_hash, '_default')
                                     ORDER BY time
                                 ) AS rn
                          FROM history WHERE date = @date
                      ) WHERE rn <= 10
                  )
                  """;
            AddParam(trimPerHash, "@date", date);
            await trimPerHash.ExecuteNonQueryAsync();
        }

        // Cap at 50 per date
        await using (var cap = conn.CreateCommand())
        {
            cap.Transaction = tx;
            cap.CommandText = _useSqlServer
                ? """
                  DELETE FROM history
                  WHERE date = @date AND id NOT IN (
                      SELECT TOP 50 id FROM history
                      WHERE date = @date
                      ORDER BY time
                  )
                  """
                : """
                  DELETE FROM history
                  WHERE date = @date AND rowid NOT IN (
                      SELECT rowid FROM history
                      WHERE date = @date
                      ORDER BY time LIMIT 50
                  )
                  """;
            AddParam(cap, "@date", date);
            await cap.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    // GET /leaderboard/history?days=N
    public async Task<Dictionary<string, List<HistoryRecord>>> GetHistoryAsync(int days)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var earliest = today.AddDays(-(days - 1));
        if (earliest < HistoryCutoffDate) earliest = HistoryCutoffDate;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT date, name, time, timestamp, puzzle_hash, puzzle_size, hints_used, word_hints_used, user_id
            FROM history
            WHERE date >= @earliest AND date <= @today
            ORDER BY date DESC, time
            """;
        AddParam(cmd, "@earliest", earliest.ToString("yyyy-MM-dd"));
        AddParam(cmd, "@today", today.ToString("yyyy-MM-dd"));

        var result = new Dictionary<string, List<HistoryRecord>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var date = reader.GetString(0);
            var record = new HistoryRecord(
                Name: reader.GetString(1),
                Time: reader.GetDouble(2),
                Timestamp: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                PuzzleHash: reader.IsDBNull(4) ? null : reader.GetString(4),
                PuzzleSize: reader.IsDBNull(5) ? null : reader.GetString(5),
                HintsUsed: reader.GetInt32(6),
                WordHintsUsed: reader.GetInt32(7),
                UserId: reader.IsDBNull(8) ? null : "verified"
            );

            if (!result.TryGetValue(date, out var list))
            {
                list = [];
                result[date] = list;
            }
            list.Add(record);
        }

        return result;
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        if (!_useSqlServer)
            SqliteConnection.ClearAllPools();
    }

    // ── User Alias Management ──

    public async Task<string?> GetAliasAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT alias FROM user_aliases WHERE user_id = @uid";
        AddParam(cmd, "@uid", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<bool> IsAliasAvailableAsync(string alias, string? excludeUserId = null)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        if (excludeUserId is not null)
        {
            cmd.CommandText = _useSqlServer
                ? "SELECT COUNT(1) FROM user_aliases WHERE alias = @alias AND user_id != @uid"
                : "SELECT COUNT(1) FROM user_aliases WHERE alias = @alias COLLATE NOCASE AND user_id != @uid";
            AddParam(cmd, "@uid", excludeUserId);
        }
        else
        {
            cmd.CommandText = _useSqlServer
                ? "SELECT COUNT(1) FROM user_aliases WHERE alias = @alias"
                : "SELECT COUNT(1) FROM user_aliases WHERE alias = @alias COLLATE NOCASE";
        }
        AddParam(cmd, "@alias", alias);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return count == 0;
    }

    public async Task SetAliasAsync(string userId, string alias)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              MERGE user_aliases AS tgt
              USING (SELECT @uid AS user_id, @alias AS alias) AS src
              ON tgt.user_id = src.user_id
              WHEN MATCHED THEN UPDATE SET alias = src.alias
              WHEN NOT MATCHED THEN INSERT (user_id, alias) VALUES (src.user_id, src.alias);
              """
            : """
              INSERT INTO user_aliases (user_id, alias) VALUES (@uid, @alias)
              ON CONFLICT(user_id) DO UPDATE SET alias = @alias
              """;
        AddParam(cmd, "@uid", userId);
        AddParam(cmd, "@alias", alias);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<UserStatsResponse> GetUserStatsAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT date, time, puzzle_size, hints_used, word_hints_used
            FROM history
            WHERE user_id = @uid AND date >= @cutoff
            ORDER BY date DESC
            """;
        AddParam(cmd, "@uid", userId);
        AddParam(cmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd"));

        var solves = new List<UserSolveRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            solves.Add(new UserSolveRecord(
                Date: reader.GetString(0),
                Time: reader.GetDouble(1),
                PuzzleSize: reader.IsDBNull(2) ? null : reader.GetString(2),
                HintsUsed: reader.GetInt32(3),
                WordHintsUsed: reader.GetInt32(4)
            ));
        }

        if (solves.Count == 0)
            return new UserStatsResponse(0, 0, 0, 0, 0, []);

        var totalSolved = solves.Count;
        var avgTime = Math.Round(solves.Average(s => s.Time), 1);
        var bestTime = Math.Round(solves.Min(s => s.Time), 1);

        var dates = solves.Select(s => DateOnly.ParseExact(s.Date, "yyyy-MM-dd"))
                         .Distinct().OrderDescending().ToList();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        int currentStreak = 0;
        var expected = today;
        if (dates.Count > 0 && dates[0] < today)
            expected = today.AddDays(-1);

        foreach (var d in dates)
        {
            if (d == expected)
            {
                currentStreak++;
                expected = expected.AddDays(-1);
            }
            else if (d < expected)
                break;
        }

        int bestStreak = 0, streak = 0;
        DateOnly? prev = null;
        foreach (var d in dates.OrderBy(d => d))
        {
            if (prev.HasValue && d == prev.Value.AddDays(1))
                streak++;
            else
                streak = 1;
            if (streak > bestStreak) bestStreak = streak;
            prev = d;
        }

        var recent = solves.Take(30).ToList();
        return new UserStatsResponse(totalSolved, avgTime, bestTime, currentStreak, bestStreak, recent);
    }

    // ── Analytics ──

    public async Task<AnalyticsSummary> GetAnalyticsSummaryAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*)                                          AS total_completions,
                COUNT(DISTINCT name)                              AS unique_players,
                COUNT(DISTINCT date)                              AS days_with_data,
                COALESCE(AVG(time), 0)                            AS avg_time,
                COALESCE(MIN(time), 0)                            AS best_time,
                COALESCE(AVG(CASE WHEN hints_used > 0 THEN 1.0 ELSE 0.0 END), 0)      AS hint_rate,
                COALESCE(AVG(CASE WHEN word_hints_used > 0 THEN 1.0 ELSE 0.0 END), 0) AS word_hint_rate
            FROM history
            WHERE date >= @cutoff
            """;
        AddParam(cmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd"));

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new AnalyticsSummary(
            TotalCompletions: reader.GetInt32(0),
            UniquePlayers: reader.GetInt32(1),
            DaysWithData: reader.GetInt32(2),
            AverageTime: Math.Round(reader.GetDouble(3), 1),
            BestTime: Math.Round(reader.GetDouble(4), 1),
            HintRate: Math.Round(reader.GetDouble(5), 3),
            WordHintRate: Math.Round(reader.GetDouble(6), 3)
        );
    }

    public async Task<List<DailyAnalytics>> GetDailyAnalyticsAsync(int days)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var earliest = today.AddDays(-(days - 1));
        if (earliest < HistoryCutoffDate) earliest = HistoryCutoffDate;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                date,
                COUNT(*)             AS completions,
                COUNT(DISTINCT name) AS unique_players,
                AVG(time)            AS avg_time,
                MIN(time)            AS best_time,
                AVG(CASE WHEN hints_used > 0 THEN 1.0 ELSE 0.0 END) AS hint_rate
            FROM history
            WHERE date >= @earliest AND date <= @today
            GROUP BY date
            ORDER BY date DESC
            """;
        AddParam(cmd, "@earliest", earliest.ToString("yyyy-MM-dd"));
        AddParam(cmd, "@today", today.ToString("yyyy-MM-dd"));

        var result = new List<DailyAnalytics>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DailyAnalytics(
                Date: reader.GetString(0),
                Completions: reader.GetInt32(1),
                UniquePlayers: reader.GetInt32(2),
                AverageTime: Math.Round(reader.GetDouble(3), 1),
                BestTime: Math.Round(reader.GetDouble(4), 1),
                HintRate: Math.Round(reader.GetDouble(5), 3)
            ));
        }

        return result;
    }

    public async Task<List<TopPlayer>> GetTopPlayersAsync(int limit)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              SELECT TOP (@limit)
                  name,
                  COUNT(*)  AS games_played,
                  AVG(time) AS avg_time,
                  MIN(time) AS best_time,
                  AVG(CASE WHEN hints_used > 0 THEN 1.0 ELSE 0.0 END) AS hint_rate
              FROM history
              WHERE date >= @cutoff
              GROUP BY name
              ORDER BY games_played DESC, avg_time ASC
              """
            : """
              SELECT
                  name,
                  COUNT(*)  AS games_played,
                  AVG(time) AS avg_time,
                  MIN(time) AS best_time,
                  AVG(CASE WHEN hints_used > 0 THEN 1.0 ELSE 0.0 END) AS hint_rate
              FROM history
              WHERE date >= @cutoff
              GROUP BY name
              ORDER BY games_played DESC, avg_time ASC
              LIMIT @limit
              """;
        AddParam(cmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd"));
        AddParam(cmd, "@limit", limit);

        var result = new List<TopPlayer>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TopPlayer(
                Name: reader.GetString(0),
                GamesPlayed: reader.GetInt32(1),
                AverageTime: Math.Round(reader.GetDouble(2), 1),
                BestTime: Math.Round(reader.GetDouble(3), 1),
                HintRate: Math.Round(reader.GetDouble(4), 3)
            ));
        }

        return result;
    }

    // ── Friends ──

    public async Task<string?> GetUserIdByAliasAsync(string alias)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? "SELECT user_id FROM user_aliases WHERE alias = @alias"
            : "SELECT user_id FROM user_aliases WHERE alias = @alias COLLATE NOCASE";
        AddParam(cmd, "@alias", alias);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<(bool Success, string Error)> SendFriendRequestAsync(string fromUserId, string toUserId)
    {
        if (fromUserId == toUserId)
            return (false, "Du kan inte lägga till dig själv");

        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var check = conn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = """
            SELECT status FROM friend_requests
            WHERE (from_user_id = @from AND to_user_id = @to)
               OR (from_user_id = @to AND to_user_id = @from)
            """;
        AddParam(check, "@from", fromUserId);
        AddParam(check, "@to", toUserId);

        await using var reader = await check.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var status = reader.GetString(0);
            await reader.CloseAsync();
            await tx.RollbackAsync();
            if (status == "accepted") return (false, "Ni är redan vänner");
            if (status == "pending") return (false, "En vänförfrågan finns redan");
            if (status == "declined") return (false, "En vänförfrågan har redan avböjts");
        }
        await reader.CloseAsync();

        await using var countCheck = conn.CreateCommand();
        countCheck.Transaction = tx;
        countCheck.CommandText = "SELECT COUNT(*) FROM friend_requests WHERE from_user_id = @from AND status = 'pending'";
        AddParam(countCheck, "@from", fromUserId);
        var pendingCount = Convert.ToInt64(await countCheck.ExecuteScalarAsync());
        if (pendingCount >= 50)
        {
            await tx.RollbackAsync();
            return (false, "Du har för många väntande förfrågningar");
        }

        await using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO friend_requests (id, from_user_id, to_user_id, status, created_at)
            VALUES (@id, @from, @to, 'pending', @ts)
            """;
        AddParam(insert, "@id", Guid.NewGuid().ToString("N"));
        AddParam(insert, "@from", fromUserId);
        AddParam(insert, "@to", toUserId);
        AddParam(insert, "@ts", _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        await insert.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return (true, string.Empty);
    }

    public async Task<bool> AcceptFriendRequestAsync(string requestId, string currentUserId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE friend_requests SET status = 'accepted'
            WHERE id = @id AND to_user_id = @uid AND status = 'pending'
            """;
        AddParam(cmd, "@id", requestId);
        AddParam(cmd, "@uid", currentUserId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeclineFriendRequestAsync(string requestId, string currentUserId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE friend_requests SET status = 'declined'
            WHERE id = @id AND to_user_id = @uid AND status = 'pending'
            """;
        AddParam(cmd, "@id", requestId);
        AddParam(cmd, "@uid", currentUserId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RemoveFriendAsync(string currentUserId, string friendshipId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM friend_requests
            WHERE id = @id AND status = 'accepted'
              AND (from_user_id = @me OR to_user_id = @me)
            """;
        AddParam(cmd, "@id", friendshipId);
        AddParam(cmd, "@me", currentUserId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<FriendInfo>> GetFriendsAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              SELECT a.alias, f.id
              FROM friend_requests f
              JOIN user_aliases a ON a.user_id = CASE
                  WHEN f.from_user_id = @uid THEN f.to_user_id
                  ELSE f.from_user_id END
              WHERE f.status = 'accepted'
                AND (f.from_user_id = @uid OR f.to_user_id = @uid)
              ORDER BY a.alias
              """
            : """
              SELECT a.alias, f.id
              FROM friend_requests f
              JOIN user_aliases a ON a.user_id = CASE
                  WHEN f.from_user_id = @uid THEN f.to_user_id
                  ELSE f.from_user_id END
              WHERE f.status = 'accepted'
                AND (f.from_user_id = @uid OR f.to_user_id = @uid)
              ORDER BY a.alias COLLATE NOCASE
              """;
        AddParam(cmd, "@uid", userId);

        var list = new List<FriendInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new FriendInfo(reader.GetString(0), reader.GetString(1)));
        return list;
    }

    public async Task<List<FriendRequestInfo>> GetPendingRequestsAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT f.id,
                   COALESCE(fa.alias, ''),
                   COALESCE(ta.alias, ''),
                   CASE WHEN f.to_user_id = @uid THEN 'incoming' ELSE 'outgoing' END,
                   f.status,
                   f.created_at
            FROM friend_requests f
            LEFT JOIN user_aliases fa ON fa.user_id = f.from_user_id
            LEFT JOIN user_aliases ta ON ta.user_id = f.to_user_id
            WHERE f.status = 'pending'
              AND (f.from_user_id = @uid OR f.to_user_id = @uid)
            ORDER BY f.created_at DESC
            """;
        AddParam(cmd, "@uid", userId);

        var list = new List<FriendRequestInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new FriendRequestInfo(
                reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt64(5)));
        return list;
    }

    public async Task<List<FriendsLeaderboardEntry>> GetFriendsLeaderboardAsync(string userId, string date)
    {
        await using var conn = await OpenConnectionAsync();

        var friendIds = new List<string> { userId };
        await using (var friendCmd = conn.CreateCommand())
        {
            friendCmd.CommandText = """
                SELECT CASE WHEN from_user_id = @uid THEN to_user_id ELSE from_user_id END
                FROM friend_requests
                WHERE status = 'accepted' AND (from_user_id = @uid OR to_user_id = @uid)
                """;
            AddParam(friendCmd, "@uid", userId);
            await using var friendReader = await friendCmd.ExecuteReaderAsync();
            while (await friendReader.ReadAsync())
                friendIds.Add(friendReader.GetString(0));
        }

        await using var cmd = conn.CreateCommand();
        var paramNames = new List<string>();
        for (int i = 0; i < friendIds.Count; i++)
        {
            var pn = $"@uid{i}";
            paramNames.Add(pn);
            AddParam(cmd, pn, friendIds[i]);
        }

        cmd.CommandText = $"""
            SELECT a.alias, h.time, h.timestamp, h.puzzle_hash, h.hints_used, h.word_hints_used
            FROM history h
            JOIN user_aliases a ON a.user_id = h.user_id
            WHERE h.date = @date AND h.user_id IN ({string.Join(',', paramNames)})
            ORDER BY h.time
            """;
        AddParam(cmd, "@date", date);

        var list = new List<FriendsLeaderboardEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new FriendsLeaderboardEntry(
                reader.GetString(0), reader.GetDouble(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5)));
        return list;
    }

    // ── Database initialisation ──

    private void InitialiseDatabase()
    {
        if (_useSqlServer)
            InitialiseSqlServerDatabase();
        else
            InitialiseSqliteDatabase();
    }

    private void InitialiseSqlServerDatabase()
    {
        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'scores')
            CREATE TABLE scores (
                id              INT IDENTITY(1,1) PRIMARY KEY,
                leaderboard_key NVARCHAR(100) NOT NULL,
                name            NVARCHAR(100) NOT NULL,
                time            FLOAT NOT NULL,
                timestamp       BIGINT NULL,
                puzzle_hash     NVARCHAR(100) NULL,
                hints_used      INT NOT NULL DEFAULT 0,
                word_hints_used INT NOT NULL DEFAULT 0,
                user_id         NVARCHAR(200) NULL
            );

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_scores_key_time')
            CREATE INDEX idx_scores_key_time ON scores (leaderboard_key, time);

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'history')
            CREATE TABLE history (
                id              INT IDENTITY(1,1) PRIMARY KEY,
                date            NVARCHAR(10) NOT NULL,
                name            NVARCHAR(100) NOT NULL,
                time            FLOAT NOT NULL,
                timestamp       BIGINT NULL,
                puzzle_hash     NVARCHAR(100) NULL,
                puzzle_size     NVARCHAR(20) NULL,
                hints_used      INT NOT NULL DEFAULT 0,
                word_hints_used INT NOT NULL DEFAULT 0,
                user_id         NVARCHAR(200) NULL
            );

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_history_date')
            CREATE INDEX idx_history_date ON history (date);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_history_user_id')
            CREATE INDEX idx_history_user_id ON history (user_id);

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'user_aliases')
            CREATE TABLE user_aliases (
                user_id NVARCHAR(200) NOT NULL PRIMARY KEY,
                alias   NVARCHAR(100) NOT NULL UNIQUE
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'friend_requests')
            CREATE TABLE friend_requests (
                id              NVARCHAR(50) NOT NULL PRIMARY KEY,
                from_user_id    NVARCHAR(200) NOT NULL,
                to_user_id      NVARCHAR(200) NOT NULL,
                status          NVARCHAR(20) NOT NULL DEFAULT 'pending',
                created_at      BIGINT NOT NULL,
                UNIQUE(from_user_id, to_user_id)
            );

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_friend_requests_to')
            CREATE INDEX idx_friend_requests_to ON friend_requests (to_user_id, status);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_friend_requests_from')
            CREATE INDEX idx_friend_requests_from ON friend_requests (from_user_id, status);
            """;
        cmd.ExecuteNonQuery();
        _logger.LogInformation("Azure SQL database initialised");
    }

    private void InitialiseSqliteDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = NORMAL;
            """;
        pragma.ExecuteNonQuery();

        using var create = conn.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS scores (
                leaderboard_key TEXT NOT NULL,
                name            TEXT NOT NULL,
                time            REAL NOT NULL,
                timestamp       INTEGER,
                puzzle_hash     TEXT,
                hints_used      INTEGER NOT NULL DEFAULT 0,
                word_hints_used INTEGER NOT NULL DEFAULT 0,
                user_id         TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_scores_key_time ON scores (leaderboard_key, time);

            CREATE TABLE IF NOT EXISTS history (
                date            TEXT NOT NULL,
                name            TEXT NOT NULL,
                time            REAL NOT NULL,
                timestamp       INTEGER,
                puzzle_hash     TEXT,
                puzzle_size     TEXT,
                hints_used      INTEGER NOT NULL DEFAULT 0,
                word_hints_used INTEGER NOT NULL DEFAULT 0,
                user_id         TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_history_date ON history (date);
            CREATE INDEX IF NOT EXISTS idx_history_user_id ON history (user_id);

            CREATE TABLE IF NOT EXISTS user_aliases (
                user_id TEXT NOT NULL PRIMARY KEY,
                alias   TEXT NOT NULL COLLATE NOCASE,
                UNIQUE(alias)
            );

            CREATE TABLE IF NOT EXISTS friend_requests (
                id              TEXT NOT NULL PRIMARY KEY,
                from_user_id    TEXT NOT NULL,
                to_user_id      TEXT NOT NULL,
                status          TEXT NOT NULL DEFAULT 'pending',
                created_at      INTEGER NOT NULL,
                UNIQUE(from_user_id, to_user_id)
            );
            CREATE INDEX IF NOT EXISTS idx_friend_requests_to ON friend_requests (to_user_id, status);
            CREATE INDEX IF NOT EXISTS idx_friend_requests_from ON friend_requests (from_user_id, status);
            """;
        create.ExecuteNonQuery();
    }

    // ── JSON migration (SQLite only) ──

    private void MigrateFromJsonFiles()
    {
        var currentJsonPath = Path.Combine(_dataDir, "current.json");
        var historyDir = Path.Combine(_dataDir, "history");
        if (!File.Exists(currentJsonPath) && !Directory.Exists(historyDir))
            return;

        _logger.LogInformation("Found legacy JSON files — starting migration to SQLite");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
        pragma.ExecuteNonQuery();

        using var tx = conn.BeginTransaction();
        var scoresImported = 0;
        var historyImported = 0;

        try
        {
            if (File.Exists(currentJsonPath))
            {
                var json = File.ReadAllText(currentJsonPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("scores", out var scores)
                    && scores.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in scores.EnumerateObject())
                    {
                        var records = JsonSerializer.Deserialize<List<ScoreRecord>>(prop.Value.GetRawText(), JsonOptions);
                        if (records is null) continue;

                        foreach (var r in records)
                        {
                            using var insert = conn.CreateCommand();
                            insert.CommandText = """
                                INSERT INTO scores (leaderboard_key, name, time, timestamp, puzzle_hash, hints_used, word_hints_used)
                                VALUES (@key, @name, @time, @timestamp, @hash, @hints, @wordHints)
                                """;
                            insert.Parameters.AddWithValue("@key", prop.Name);
                            insert.Parameters.AddWithValue("@name", r.Name);
                            insert.Parameters.AddWithValue("@time", r.Time);
                            insert.Parameters.AddWithValue("@timestamp", (object?)r.Timestamp ?? DBNull.Value);
                            insert.Parameters.AddWithValue("@hash", (object?)r.PuzzleHash ?? DBNull.Value);
                            insert.Parameters.AddWithValue("@hints", r.HintsUsed);
                            insert.Parameters.AddWithValue("@wordHints", r.WordHintsUsed);
                            insert.ExecuteNonQuery();
                            scoresImported++;
                        }
                    }
                }
            }

            if (Directory.Exists(historyDir))
            {
                foreach (var file in Directory.EnumerateFiles(historyDir, "*.json"))
                {
                    var date = Path.GetFileNameWithoutExtension(file);
                    if (!DatePattern.IsMatch(date)) continue;

                    var json = File.ReadAllText(file);
                    var records = JsonSerializer.Deserialize<List<HistoryRecord>>(json, JsonOptions);
                    if (records is null) continue;

                    foreach (var r in records)
                    {
                        using var insert = conn.CreateCommand();
                        insert.CommandText = """
                            INSERT INTO history (date, name, time, timestamp, puzzle_hash, puzzle_size, hints_used, word_hints_used)
                            VALUES (@date, @name, @time, @timestamp, @hash, @size, @hints, @wordHints)
                            """;
                        insert.Parameters.AddWithValue("@date", date);
                        insert.Parameters.AddWithValue("@name", r.Name);
                        insert.Parameters.AddWithValue("@time", r.Time);
                        insert.Parameters.AddWithValue("@timestamp", (object?)r.Timestamp ?? DBNull.Value);
                        insert.Parameters.AddWithValue("@hash", (object?)r.PuzzleHash ?? DBNull.Value);
                        insert.Parameters.AddWithValue("@size", (object?)r.PuzzleSize ?? DBNull.Value);
                        insert.Parameters.AddWithValue("@hints", r.HintsUsed);
                        insert.Parameters.AddWithValue("@wordHints", r.WordHintsUsed);
                        insert.ExecuteNonQuery();
                        historyImported++;
                    }
                }
            }

            tx.Commit();
            _logger.LogInformation("Migration complete — imported {Scores} score rows and {History} history rows", scoresImported, historyImported);

            if (File.Exists(currentJsonPath))
                File.Move(currentJsonPath, currentJsonPath + ".migrated");
            if (Directory.Exists(historyDir))
                Directory.Move(historyDir, historyDir + ".migrated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON-to-SQLite migration failed — rolling back");
            tx.Rollback();
        }
    }

    // ── Connection helpers ──

    private async Task<DbConnection> OpenConnectionAsync()
    {
        DbConnection conn = _useSqlServer
            ? new SqlConnection(_connectionString)
            : new SqliteConnection(_connectionString);

        await conn.OpenAsync();

        if (!_useSqlServer)
        {
            await using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL;";
            await pragma.ExecuteNonQueryAsync();
        }

        return conn;
    }

    private async Task<List<ScoreRecord>> GetScoresForKeyAsync(DbConnection conn, DbTransaction? tx, string leaderboardKey)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT name, time, timestamp, puzzle_hash, hints_used, word_hints_used, user_id
            FROM scores
            WHERE leaderboard_key = @key
            ORDER BY time
            """;
        AddParam(cmd, "@key", leaderboardKey);

        var list = new List<ScoreRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var hasUserId = !reader.IsDBNull(6);
            list.Add(new ScoreRecord(
                Name: reader.GetString(0),
                Time: reader.GetDouble(1),
                Timestamp: reader.IsDBNull(2) ? null : reader.GetInt64(2),
                PuzzleHash: reader.IsDBNull(3) ? null : reader.GetString(3),
                HintsUsed: reader.GetInt32(4),
                WordHintsUsed: reader.GetInt32(5),
                UserId: hasUserId ? "verified" : null
            ));
        }

        return list;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
