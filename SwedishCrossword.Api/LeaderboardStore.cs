using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SwedishCrossword.Api;

/// <summary>
/// SQLite-based leaderboard, history, user alias, and friends storage.
/// On Azure Files SMB the app falls back to DELETE journal mode automatically
/// because WAL requires mmap support (not available on SMB). For WAL support,
/// switch to Azure Files NFS (Premium tier + VNet).
/// </summary>
sealed class LeaderboardStore : IDisposable
{
    public static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    /// <summary>
    /// Data before this date is discarded (leaderboard reset).
    /// </summary>
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

    public LeaderboardStore(IConfiguration config, ILogger<LeaderboardStore> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;

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
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitialiseDatabase();
        MigrateFromJsonFiles();
    }

    public static string SanitiseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sanitised = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return sanitised[..Math.Min(sanitised.Length, 30)];
    }

    // GET /leaderboard — return the current leaderboard JSON
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
                UserId: hasUserId ? "verified" : null // Don't expose actual user IDs publicly
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

    // POST /api/scores — append a validated score to the leaderboard
    public async Task<List<ScoreRecord>> AppendScoreAsync(string leaderboardKey, ScoreRecord entry)
    {
        await using var conn = await OpenConnectionAsync();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

        // Deduplicate
        await using (var dedup = conn.CreateCommand())
        {
            dedup.CommandText = """
                SELECT COUNT(1) FROM scores
                WHERE leaderboard_key = @key AND name = @name
                  AND ABS(time - @time) < 0.001
                  AND timestamp IS @timestamp
                """;
            dedup.Parameters.AddWithValue("@key", leaderboardKey);
            dedup.Parameters.AddWithValue("@name", entry.Name);
            dedup.Parameters.AddWithValue("@time", entry.Time);
            dedup.Parameters.AddWithValue("@timestamp", (object?)entry.Timestamp ?? DBNull.Value);

            var count = (long)(await dedup.ExecuteScalarAsync())!;
            if (count > 0)
            {
                await tx.CommitAsync();
                return await GetScoresForKeyAsync(conn, leaderboardKey);
            }
        }

        // Insert
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO scores (leaderboard_key, name, time, timestamp, puzzle_hash, hints_used, word_hints_used, user_id)
                VALUES (@key, @name, @time, @timestamp, @hash, @hints, @wordHints, @userId)
                """;
            insert.Parameters.AddWithValue("@key", leaderboardKey);
            insert.Parameters.AddWithValue("@name", entry.Name);
            insert.Parameters.AddWithValue("@time", entry.Time);
            insert.Parameters.AddWithValue("@timestamp", (object?)entry.Timestamp ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hash", (object?)entry.PuzzleHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hints", entry.HintsUsed);
            insert.Parameters.AddWithValue("@wordHints", entry.WordHintsUsed);
            insert.Parameters.AddWithValue("@userId", (object?)entry.UserId ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        // Keep top 10 per key
        await using (var trim = conn.CreateCommand())
        {
            trim.CommandText = """
                DELETE FROM scores
                WHERE leaderboard_key = @key
                  AND rowid NOT IN (
                    SELECT rowid FROM scores
                    WHERE leaderboard_key = @key
                    ORDER BY time LIMIT 10
                  )
                """;
            trim.Parameters.AddWithValue("@key", leaderboardKey);
            await trim.ExecuteNonQueryAsync();
        }

        // Prune entries older than 7 days or before the history cutoff date
        var cutoff = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(-7);
        if (cutoff < HistoryCutoffDate) cutoff = HistoryCutoffDate;
        await using (var prune = conn.CreateCommand())
        {
            prune.CommandText = """
                DELETE FROM scores
                WHERE SUBSTR(leaderboard_key, 1, 10) < @cutoff
                """;
            prune.Parameters.AddWithValue("@cutoff", cutoff.ToString("yyyy-MM-dd"));
            await prune.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return await GetScoresForKeyAsync(conn, leaderboardKey);
    }

    // POST /leaderboard/history — append a record for a specific date
    public async Task AppendHistoryAsync(string date, HistoryRecord record)
    {
        await using var conn = await OpenConnectionAsync();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();

        // Deduplicate
        await using (var dedup = conn.CreateCommand())
        {
            dedup.CommandText = """
                SELECT COUNT(1) FROM history
                WHERE date = @date AND name = @name
                  AND ABS(time - @time) < 0.001
                  AND timestamp IS @timestamp
                """;
            dedup.Parameters.AddWithValue("@date", date);
            dedup.Parameters.AddWithValue("@name", record.Name);
            dedup.Parameters.AddWithValue("@time", record.Time);
            dedup.Parameters.AddWithValue("@timestamp", (object?)record.Timestamp ?? DBNull.Value);

            var count = (long)(await dedup.ExecuteScalarAsync())!;
            if (count > 0)
            {
                await tx.CommitAsync();
                return;
            }
        }

        // Insert
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO history (date, name, time, timestamp, puzzle_hash, puzzle_size, hints_used, word_hints_used, user_id)
                VALUES (@date, @name, @time, @timestamp, @hash, @size, @hints, @wordHints, @userId)
                """;
            insert.Parameters.AddWithValue("@date", date);
            insert.Parameters.AddWithValue("@name", record.Name);
            insert.Parameters.AddWithValue("@time", record.Time);
            insert.Parameters.AddWithValue("@timestamp", (object?)record.Timestamp ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hash", (object?)record.PuzzleHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("@size", (object?)record.PuzzleSize ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hints", record.HintsUsed);
            insert.Parameters.AddWithValue("@wordHints", record.WordHintsUsed);
            insert.Parameters.AddWithValue("@userId", (object?)record.UserId ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        // Keep top 10 per puzzle_hash for this date
        await using (var trimPerHash = conn.CreateCommand())
        {
            trimPerHash.CommandText = """
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
            trimPerHash.Parameters.AddWithValue("@date", date);
            await trimPerHash.ExecuteNonQueryAsync();
        }

        // Cap at 50 per date
        await using (var cap = conn.CreateCommand())
        {
            cap.CommandText = """
                DELETE FROM history
                WHERE date = @date AND rowid NOT IN (
                    SELECT rowid FROM history
                    WHERE date = @date
                    ORDER BY time LIMIT 50
                )
                """;
            cap.Parameters.AddWithValue("@date", date);
            await cap.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    // GET /leaderboard/history?days=N — return historical data
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
        cmd.Parameters.AddWithValue("@earliest", earliest.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@today", today.ToString("yyyy-MM-dd"));

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
        SqliteConnection.ClearAllPools();
    }

    // ── User Alias Management ──

    public async Task<string?> GetAliasAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT alias FROM user_aliases WHERE user_id = @uid";
        cmd.Parameters.AddWithValue("@uid", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<bool> IsAliasAvailableAsync(string alias, string? excludeUserId = null)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        if (excludeUserId is not null)
        {
            cmd.CommandText = "SELECT COUNT(1) FROM user_aliases WHERE alias = @alias COLLATE NOCASE AND user_id != @uid";
            cmd.Parameters.AddWithValue("@uid", excludeUserId);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(1) FROM user_aliases WHERE alias = @alias COLLATE NOCASE";
        }
        cmd.Parameters.AddWithValue("@alias", alias);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count == 0;
    }

    public async Task SetAliasAsync(string userId, string alias)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_aliases (user_id, alias) VALUES (@uid, @alias)
            ON CONFLICT(user_id) DO UPDATE SET alias = @alias
            """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@alias", alias);
        await cmd.ExecuteNonQueryAsync();
    }

    // Personal stats for an authenticated user
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
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd"));

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

        // Compute streak from distinct dates (descending)
        var dates = solves.Select(s => DateOnly.ParseExact(s.Date, "yyyy-MM-dd"))
                         .Distinct().OrderDescending().ToList();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        int currentStreak = 0;
        var expected = today;
        // Allow starting from today or yesterday
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
            {
                break;
            }
        }

        // Best streak over all time
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

    // Analytics: overall summary from history table
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
        cmd.Parameters.AddWithValue("@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd"));

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

    // Analytics: per-day breakdown for the last N days
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
        cmd.Parameters.AddWithValue("@earliest", earliest.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@today", today.ToString("yyyy-MM-dd"));

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

    // Analytics: top players ranked by games played
    public async Task<List<TopPlayer>> GetTopPlayersAsync(int limit)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
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
        cmd.Parameters.AddWithValue("@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@limit", limit);

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
            // --- Migrate current.json → scores table ---
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

            // --- Migrate history/{date}.json → history table ---
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
            _logger.LogInformation(
                "Migration complete — imported {Scores} score rows and {History} history rows",
                scoresImported, historyImported);

            // Rename old files so migration won't run again
            if (File.Exists(currentJsonPath))
                File.Move(currentJsonPath, currentJsonPath + ".migrated");
            if (Directory.Exists(historyDir))
                Directory.Move(historyDir, historyDir + ".migrated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON-to-SQLite migration failed — rolling back. Old files left in place for retry on next startup");
            tx.Rollback();
        }
    }

    private void InitialiseDatabase()
    {
        // Retry with back-off — during deployments the previous container revision
        // may still hold an SMB file lock on Azure Files for several seconds.
        const int maxRetries = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                InitialiseDatabaseCore();
                return;
            }
            catch (SqliteException ex) when (attempt < maxRetries && ex.SqliteErrorCode == 5 /* SQLITE_BUSY */)
            {
                _logger.LogWarning(ex, "Database locked during initialisation (attempt {Attempt}/{Max}), retrying...", attempt, maxRetries);
                Thread.Sleep(attempt * 2000);
            }
        }
    }

    private void InitialiseDatabaseCore()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Try WAL mode first (better concurrency), fall back to DELETE mode
        // if it fails — Azure Files SMB doesn't support the shared-memory
        // file that WAL requires.
        using (var walPragma = conn.CreateCommand())
        {
            walPragma.CommandText = "PRAGMA journal_mode = WAL;";
            try
            {
                walPragma.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "WAL mode not supported on this storage, using DELETE journal mode");
                using var deletePragma = conn.CreateCommand();
                deletePragma.CommandText = "PRAGMA journal_mode = DELETE;";
                deletePragma.ExecuteNonQuery();
            }
        }

        using var pragma = conn.CreateCommand();
        pragma.CommandText = """
            PRAGMA busy_timeout = 30000;
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

            CREATE INDEX IF NOT EXISTS idx_scores_key_time
            ON scores (leaderboard_key, time);

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

            CREATE INDEX IF NOT EXISTS idx_history_date
            ON history (date);
            """;
        create.ExecuteNonQuery();

        // Add user_id column if missing (migration for existing databases)
        MigrateAddColumn(conn, "scores", "user_id", "TEXT");
        MigrateAddColumn(conn, "history", "user_id", "TEXT");

        // Create index on user_id *after* migration ensures the column exists
        using var createUserIdIndex = conn.CreateCommand();
        createUserIdIndex.CommandText = "CREATE INDEX IF NOT EXISTS idx_history_user_id ON history (user_id);";
        createUserIdIndex.ExecuteNonQuery();

        // User aliases table — unique alias per authenticated user
        using var createAliases = conn.CreateCommand();
        createAliases.CommandText = """
            CREATE TABLE IF NOT EXISTS user_aliases (
                user_id TEXT NOT NULL PRIMARY KEY,
                alias   TEXT NOT NULL COLLATE NOCASE,
                UNIQUE(alias)
            );
            """;
        createAliases.ExecuteNonQuery();

        using var createFriends = conn.CreateCommand();
        createFriends.CommandText = """
            CREATE TABLE IF NOT EXISTS friend_requests (
                id              TEXT NOT NULL PRIMARY KEY,
                from_user_id    TEXT NOT NULL,
                to_user_id      TEXT NOT NULL,
                status          TEXT NOT NULL DEFAULT 'pending',
                created_at      INTEGER NOT NULL,
                UNIQUE(from_user_id, to_user_id)
            );

            CREATE INDEX IF NOT EXISTS idx_friend_requests_to
            ON friend_requests (to_user_id, status);

            CREATE INDEX IF NOT EXISTS idx_friend_requests_from
            ON friend_requests (from_user_id, status);
            """;
        createFriends.ExecuteNonQuery();
    }

    private static void MigrateAddColumn(SqliteConnection conn, string table, string column, string type)
    {
        bool exists = false;
        using (var check = conn.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alter.ExecuteNonQuery();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var pragma = conn.CreateCommand();
        pragma.CommandText = """
            PRAGMA busy_timeout = 30000;
            PRAGMA synchronous = NORMAL;
            """;
        await pragma.ExecuteNonQueryAsync();

        return conn;
    }

    private static async Task<List<ScoreRecord>> GetScoresForKeyAsync(SqliteConnection conn, string leaderboardKey)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name, time, timestamp, puzzle_hash, hints_used, word_hints_used, user_id
            FROM scores
            WHERE leaderboard_key = @key
            ORDER BY time
            """;
        cmd.Parameters.AddWithValue("@key", leaderboardKey);

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

    // -----------------------------------------------------------------------
    // Friends
    // -----------------------------------------------------------------------

    public async Task<string?> GetUserIdByAliasAsync(string alias)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id FROM user_aliases WHERE alias = @alias COLLATE NOCASE";
        cmd.Parameters.AddWithValue("@alias", alias);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task<(bool Success, string Error)> SendFriendRequestAsync(string fromUserId, string toUserId)
    {
        if (fromUserId == toUserId)
            return (false, "Du kan inte lägga till dig själv");

        await using var conn = await OpenConnectionAsync();

        // Use a transaction to prevent TOCTOU races
        await using var tx = await conn.BeginTransactionAsync();

        // Check for existing relationship in either direction
        await using var check = conn.CreateCommand();
        check.Transaction = (SqliteTransaction)tx;
        check.CommandText = """
            SELECT status FROM friend_requests
            WHERE (from_user_id = @from AND to_user_id = @to)
               OR (from_user_id = @to AND to_user_id = @from)
            """;
        check.Parameters.AddWithValue("@from", fromUserId);
        check.Parameters.AddWithValue("@to", toUserId);

        await using var reader = await check.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var status = reader.GetString(0);
            await reader.CloseAsync();
            await tx.RollbackAsync();
            if (status == "accepted")
                return (false, "Ni är redan vänner");
            if (status == "pending")
                return (false, "En vänförfrågan finns redan");
            if (status == "declined")
                return (false, "En vänförfrågan har redan avböjts");
        }
        await reader.CloseAsync();

        // Limit pending outgoing requests to 50
        await using var countCheck = conn.CreateCommand();
        countCheck.Transaction = (SqliteTransaction)tx;
        countCheck.CommandText = "SELECT COUNT(*) FROM friend_requests WHERE from_user_id = @from AND status = 'pending'";
        countCheck.Parameters.AddWithValue("@from", fromUserId);
        var pendingCount = (long)(await countCheck.ExecuteScalarAsync())!;
        if (pendingCount >= 50)
        {
            await tx.RollbackAsync();
            return (false, "Du har för många väntande förfrågningar");
        }

        await using var insert = conn.CreateCommand();
        insert.Transaction = (SqliteTransaction)tx;
        insert.CommandText = """
            INSERT INTO friend_requests (id, from_user_id, to_user_id, status, created_at)
            VALUES (@id, @from, @to, 'pending', @ts)
            """;
        insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("@from", fromUserId);
        insert.Parameters.AddWithValue("@to", toUserId);
        insert.Parameters.AddWithValue("@ts", _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
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
        cmd.Parameters.AddWithValue("@id", requestId);
        cmd.Parameters.AddWithValue("@uid", currentUserId);
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
        cmd.Parameters.AddWithValue("@id", requestId);
        cmd.Parameters.AddWithValue("@uid", currentUserId);
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
        cmd.Parameters.AddWithValue("@id", friendshipId);
        cmd.Parameters.AddWithValue("@me", currentUserId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<FriendInfo>> GetFriendsAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.alias, f.id
            FROM friend_requests f
            JOIN user_aliases a ON a.user_id = CASE
                WHEN f.from_user_id = @uid THEN f.to_user_id
                ELSE f.from_user_id END
            WHERE f.status = 'accepted'
              AND (f.from_user_id = @uid OR f.to_user_id = @uid)
            ORDER BY a.alias COLLATE NOCASE
            """;
        cmd.Parameters.AddWithValue("@uid", userId);

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
        cmd.Parameters.AddWithValue("@uid", userId);

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

        // Get accepted friend user IDs using the same connection
        var friendIds = new List<string> { userId };
        await using (var friendCmd = conn.CreateCommand())
        {
            friendCmd.CommandText = """
                SELECT CASE WHEN from_user_id = @uid THEN to_user_id ELSE from_user_id END
                FROM friend_requests
                WHERE status = 'accepted' AND (from_user_id = @uid OR to_user_id = @uid)
                """;
            friendCmd.Parameters.AddWithValue("@uid", userId);
            await using var friendReader = await friendCmd.ExecuteReaderAsync();
            while (await friendReader.ReadAsync())
                friendIds.Add(friendReader.GetString(0));
        }

        await using var cmd = conn.CreateCommand();

        // Build parameterised IN clause
        var paramNames = new List<string>();
        for (int i = 0; i < friendIds.Count; i++)
        {
            var pn = $"@uid{i}";
            paramNames.Add(pn);
            cmd.Parameters.AddWithValue(pn, friendIds[i]);
        }

        cmd.CommandText = $"""
            SELECT a.alias, h.time, h.timestamp, h.puzzle_hash, h.hints_used, h.word_hints_used
            FROM history h
            JOIN user_aliases a ON a.user_id = h.user_id
            WHERE h.date = @date AND h.user_id IN ({string.Join(',', paramNames)})
            ORDER BY h.time
            """;
        cmd.Parameters.AddWithValue("@date", date);

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
}
