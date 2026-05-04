using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;

namespace SwedishCrossword.Api;

/// <summary>
/// Leaderboard, history, user alias, and friends storage.
/// Uses Azure SQL in production (when ConnectionStrings:Leaderboard is set)
/// and falls back to SQLite for local development and testing.
/// </summary>
sealed partial class LeaderboardStore : IScoreStore, IHistoryStore, IUserProfileStore, IFriendStore, IAnalyticsStore, IAdminStore, IClueFlagStore, IDisposable
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    public static partial Regex DatePattern { get; }

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
    private readonly bool _useSqlServer;
    private readonly MemoryCache _aliasCache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private static readonly TimeSpan AliasCacheDuration = TimeSpan.FromMinutes(5);

    public LeaderboardStore(IConfiguration config, ILogger<LeaderboardStore> logger, TimeProvider timeProvider, IHostEnvironment environment)
    {
        _logger = logger;
        _timeProvider = timeProvider;

        var sqlConnStr = config.GetConnectionString("Leaderboard");
        _useSqlServer = !string.IsNullOrWhiteSpace(sqlConnStr);

        if (_useSqlServer)
        {
            var sqlBuilder = new SqlConnectionStringBuilder(sqlConnStr!);
            if (sqlBuilder.ConnectRetryCount == 1)
                sqlBuilder.ConnectRetryCount = 3;
            if (sqlBuilder.ConnectRetryInterval == 10)
                sqlBuilder.ConnectRetryInterval = 5;
            // Serverless Azure SQL can take 30–60s to resume from auto-pause.
            // Ensure the per-connection login timeout is high enough even if the
            // connection string was supplied without one (default is 15s).
            if (sqlBuilder.ConnectTimeout < 60)
                sqlBuilder.ConnectTimeout = 60;
            _connectionString = sqlBuilder.ToString();
            _dataDir = string.Empty;
            _logger.LogInformation("Using Azure SQL for leaderboard storage");
        }
        else if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Azure SQL connection string 'ConnectionStrings:Leaderboard' is required in non-Development environments. " +
                "SQLite is only supported for local development.");
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
        // Fast path: check if any control characters exist
        var span = name.AsSpan().Trim();
        if (span.Length == 0) return string.Empty;
        var hasControl = false;
        foreach (var c in span)
        {
            if (char.IsControl(c)) { hasControl = true; break; }
        }
        if (!hasControl)
            return span.Length <= 30 ? span.ToString() : span[..30].ToString();
        // Slow path: filter control characters
        return string.Create(Math.Min(span.Length, 30), span, static (dest, src) =>
        {
            int written = 0;
            foreach (var c in src)
            {
                if (!char.IsControl(c))
                {
                    dest[written++] = c;
                    if (written == dest.Length) break;
                }
            }
            dest[written..].Clear();
        }).TrimEnd('\0');
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

        try
        {
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

                var count = Convert.ToInt64(await dedup.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
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

            await tx.CommitAsync();
            return await GetScoresForKeyAsync(conn, null, leaderboardKey);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Prunes old score and history entries. Called periodically by <see cref="LeaderboardPruneService"/>
    /// instead of on every write, reducing submission latency.
    /// </summary>
    public async Task PruneOldEntriesAsync()
    {
        await using var conn = await OpenConnectionAsync();

        // Prune scores older than 7 days
        var scoreCutoff = _timeProvider.GetSwedishDate().AddDays(-7);
        if (scoreCutoff < HistoryCutoffDate) scoreCutoff = HistoryCutoffDate;
        await using (var prune = conn.CreateCommand())
        {
            prune.CommandText = _useSqlServer
                ? "DELETE FROM scores WHERE SUBSTRING(leaderboard_key, 1, 10) < @cutoff"
                : "DELETE FROM scores WHERE SUBSTR(leaderboard_key, 1, 10) < @cutoff";
            AddParam(prune, "@cutoff", scoreCutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            await prune.ExecuteNonQueryAsync();
        }

        // Prune history older than 365 days
        var historyCutoff = _timeProvider.GetSwedishDate().AddDays(-365);
        if (historyCutoff < HistoryCutoffDate) historyCutoff = HistoryCutoffDate;
        await using var purge = conn.CreateCommand();
        purge.CommandText = "DELETE FROM history WHERE date < @cutoff";
        AddParam(purge, "@cutoff", historyCutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await purge.ExecuteNonQueryAsync();
    }

    // POST /leaderboard/history
    public async Task AppendHistoryAsync(string date, HistoryRecord record)
    {
        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
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

                var count = Convert.ToInt64(await dedup.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
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
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // GET /leaderboard/history?days=N
    public async Task<Dictionary<string, List<HistoryRecord>>> GetHistoryAsync(int days)
    {
        var today = _timeProvider.GetSwedishDate();
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
        AddParam(cmd, "@earliest", earliest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParam(cmd, "@today", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

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
        _aliasCache.Dispose();
        if (!_useSqlServer)
            SqliteConnection.ClearAllPools();
    }

    // ── User Alias Management ──

    /// <summary>
    /// Resolves canonical user ID and lazily migrates legacy references.
    /// </summary>
    public async Task<string> ResolveCanonicalUserIdAsync(string canonicalUserId, string? legacyUserId)
    {
        if (string.IsNullOrWhiteSpace(canonicalUserId))
            throw new ArgumentException("Canonical user ID is required", nameof(canonicalUserId));

        if (string.IsNullOrWhiteSpace(legacyUserId)
            || string.Equals(canonicalUserId, legacyUserId, StringComparison.Ordinal))
            return canonicalUserId;

        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            await using (var aliasCmd = conn.CreateCommand())
            {
                aliasCmd.Transaction = tx;
                aliasCmd.CommandText = _useSqlServer
                    ? """
                      IF EXISTS (SELECT 1 FROM user_aliases WHERE user_id = @legacy)
                      BEGIN
                          IF EXISTS (SELECT 1 FROM user_aliases WHERE user_id = @canonical)
                              DELETE FROM user_aliases WHERE user_id = @legacy;
                          ELSE
                              UPDATE user_aliases SET user_id = @canonical WHERE user_id = @legacy;
                      END
                      """
                    : """
                      INSERT OR IGNORE INTO user_aliases (user_id, alias)
                      SELECT @canonical, alias FROM user_aliases WHERE user_id = @legacy;
                      DELETE FROM user_aliases WHERE user_id = @legacy;
                      """;
                AddParam(aliasCmd, "@canonical", canonicalUserId);
                AddParam(aliasCmd, "@legacy", legacyUserId);
                await aliasCmd.ExecuteNonQueryAsync();
            }

            await using (var adminCmd = conn.CreateCommand())
            {
                adminCmd.Transaction = tx;
                adminCmd.CommandText = _useSqlServer
                    ? """
                      IF EXISTS (SELECT 1 FROM admin_grants WHERE user_id = @legacy)
                      BEGIN
                          IF EXISTS (SELECT 1 FROM admin_grants WHERE user_id = @canonical)
                              DELETE FROM admin_grants WHERE user_id = @legacy;
                          ELSE
                              UPDATE admin_grants SET user_id = @canonical WHERE user_id = @legacy;
                      END
                      UPDATE admin_grants SET granted_by = @canonical WHERE granted_by = @legacy;
                      """
                    : """
                      INSERT OR IGNORE INTO admin_grants (user_id, granted_by, granted_at)
                      SELECT @canonical, granted_by, granted_at FROM admin_grants WHERE user_id = @legacy;
                      DELETE FROM admin_grants WHERE user_id = @legacy;
                      UPDATE admin_grants SET granted_by = @canonical WHERE granted_by = @legacy;
                      """;
                AddParam(adminCmd, "@canonical", canonicalUserId);
                AddParam(adminCmd, "@legacy", legacyUserId);
                await adminCmd.ExecuteNonQueryAsync();
            }

            await using (var friendsDedup = conn.CreateCommand())
            {
                friendsDedup.Transaction = tx;
                friendsDedup.CommandText = _useSqlServer
                    ? """
                      DELETE fr
                      FROM friend_requests fr
                      WHERE (fr.from_user_id = @legacy OR fr.to_user_id = @legacy)
                        AND EXISTS (
                            SELECT 1
                            FROM friend_requests other
                            WHERE other.id <> fr.id
                              AND other.from_user_id = CASE WHEN fr.from_user_id = @legacy THEN @canonical ELSE fr.from_user_id END
                              AND other.to_user_id = CASE WHEN fr.to_user_id = @legacy THEN @canonical ELSE fr.to_user_id END
                        );
                      """
                    : """
                      DELETE FROM friend_requests
                      WHERE rowid IN (
                          SELECT fr.rowid
                          FROM friend_requests fr
                          WHERE (fr.from_user_id = @legacy OR fr.to_user_id = @legacy)
                            AND EXISTS (
                                SELECT 1
                                FROM friend_requests other
                                WHERE other.rowid <> fr.rowid
                                  AND other.from_user_id = CASE WHEN fr.from_user_id = @legacy THEN @canonical ELSE fr.from_user_id END
                                  AND other.to_user_id = CASE WHEN fr.to_user_id = @legacy THEN @canonical ELSE fr.to_user_id END
                            )
                      );
                      """;
                AddParam(friendsDedup, "@canonical", canonicalUserId);
                AddParam(friendsDedup, "@legacy", legacyUserId);
                await friendsDedup.ExecuteNonQueryAsync();
            }

            foreach (var (table, column) in new []
            {
                ("scores", "user_id"),
                ("history", "user_id"),
                ("friend_requests", "from_user_id"),
                ("friend_requests", "to_user_id"),
                ("friend_challenges", "from_user_id"),
                ("friend_challenges", "to_user_id"),
                ("clue_flags", "created_by"),
                ("clue_flags", "reviewed_by")
            })
            {
                await using var updateCmd = conn.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = $"UPDATE {table} SET {column} = @canonical WHERE {column} = @legacy";
                AddParam(updateCmd, "@canonical", canonicalUserId);
                AddParam(updateCmd, "@legacy", legacyUserId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await using (var selfFriendCleanup = conn.CreateCommand())
            {
                selfFriendCleanup.Transaction = tx;
                selfFriendCleanup.CommandText = "DELETE FROM friend_requests WHERE from_user_id = to_user_id";
                await selfFriendCleanup.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            _aliasCache.Remove($"alias:{legacyUserId}");
            _aliasCache.Remove($"alias:{canonicalUserId}");
            return canonicalUserId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<string?> GetAliasAsync(string userId)
    {
        var cacheKey = $"alias:{userId}";
        if (_aliasCache.TryGetValue(cacheKey, out string? cached))
            return cached;

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT alias FROM user_aliases WHERE user_id = @uid";
        AddParam(cmd, "@uid", userId);
        var result = await cmd.ExecuteScalarAsync();
        var alias = result as string;

        _aliasCache.Set(cacheKey, alias, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = AliasCacheDuration,
            Size = 1
        });

        return alias;
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
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        return count == 0;
    }

    /// <summary>
    /// Sets (or updates) the alias for a user. Returns false if the alias is
    /// already taken by another user (unique constraint violation).
    /// </summary>
    public async Task<bool> SetAliasAsync(string userId, string alias)
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

        try
        {
            await cmd.ExecuteNonQueryAsync();
            _aliasCache.Set($"alias:{userId}", (string?)alias, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = AliasCacheDuration,
                Size = 1
            });
            return true;
        }
        catch (DbException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Alias '{Alias}' uniqueness conflict for user {UserId}", alias, userId);
            return false;
        }
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
        AddParam(cmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

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
            return new UserStatsResponse(0, 0, 0, 0, 0, [], Badges: CreateBadges(0, 0, 0, []));

        var totalSolved = solves.Count;
        var avgTime = Math.Round(solves.Average(s => s.Time), 1);
        var bestTime = Math.Round(solves.Min(s => s.Time), 1);

        var today = _timeProvider.GetSwedishDate();

        var perSize = solves
            .Where(s => !string.IsNullOrEmpty(s.PuzzleSize))
            .GroupBy(s => s.PuzzleSize!)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var sizeDates = g.Select(s => DateOnly.ParseExact(s.Date, "yyyy-MM-dd")).Distinct().OrderDescending().ToList();
                    var (cur, best) = ComputeStreaks(sizeDates, today);
                    return new SizeStatsEntry(
                        Count: g.Count(),
                        AverageTime: Math.Round(g.Average(s => s.Time), 1),
                        BestTime: Math.Round(g.Min(s => s.Time), 1),
                        CurrentStreak: cur,
                        BestStreak: best
                    );
                }
            );

        var dates = solves.Select(s => DateOnly.ParseExact(s.Date, "yyyy-MM-dd"))
                         .Distinct().OrderDescending().ToList();
        var (currentStreak, bestStreak) = ComputeStreaks(dates, today);

        var recent = solves.Take(30).ToList();
        var badges = CreateBadges(totalSolved, bestTime, bestStreak, solves);
        return new UserStatsResponse(totalSolved, avgTime, bestTime, currentStreak, bestStreak, recent, perSize.Count > 0 ? perSize : null, badges);
    }

    private static List<AchievementBadge> CreateBadges(int totalSolved, double bestTime, int bestStreak, List<UserSolveRecord> solves)
    {
        var hasNoHintSolve = solves.Any(s => s.HintsUsed == 0 && s.WordHintsUsed == 0);
        var solvedSizes = solves
            .Where(s => !string.IsNullOrWhiteSpace(s.PuzzleSize))
            .Select(s => s.PuzzleSize!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasAllSizes = solvedSizes.Contains("10x10") && solvedSizes.Contains("15x15") && solvedSizes.Contains("17x17");

        return
        [
            new AchievementBadge("first-solve", "Första klar", "Lös ditt första korsord.", "🎉", totalSolved >= 1),
            new AchievementBadge("clean-solve", "Utan ledtrådar", "Lös ett korsord utan att använda några ledtrådar.", "🧠", hasNoHintSolve),
            new AchievementBadge("speed-run", "Snabb lösare", "Lös ett korsord på under 5 minuter.", "⚡", totalSolved > 0 && bestTime < 300),
            new AchievementBadge("streak-3", "3 dagar i rad", "Nå en streak på minst 3 dagar.", "🔥", bestStreak >= 3),
            new AchievementBadge("streak-7", "Veckostreak", "Nå en streak på minst 7 dagar.", "🏅", bestStreak >= 7),
            new AchievementBadge("size-explorer", "Storleksutforskare", "Lös minst ett 10×10-, 15×15- och 17×17-korsord.", "🧩", hasAllSizes)
        ];
    }

    private static (int Current, int Best) ComputeStreaks(List<DateOnly> datesDesc, DateOnly today)
    {
        int currentStreak = 0;
        var expected = today;
        if (datesDesc.Count > 0 && datesDesc[0] < today)
            expected = today.AddDays(-1);

        foreach (var d in datesDesc)
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
        foreach (var d in datesDesc.OrderBy(d => d))
        {
            if (prev.HasValue && d == prev.Value.AddDays(1))
                streak++;
            else
                streak = 1;
            if (streak > bestStreak) bestStreak = streak;
            prev = d;
        }

        return (currentStreak, bestStreak);
    }

    // ── Analytics ──

    public async Task<AnalyticsSummary> GetAnalyticsSummaryAsync()
    {
        var today = _timeProvider.GetSwedishDate().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        await using var conn = await OpenConnectionAsync();

        // Main aggregates
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COUNT(*)                                          AS total_completions,
                COUNT(DISTINCT name)                              AS unique_players,
                COALESCE(AVG(time), 0)                            AS avg_time,
                COALESCE(MIN(time), 0)                            AS best_time,
                COALESCE(AVG(CASE WHEN hints_used > 0 OR word_hints_used > 0 THEN 1.0 ELSE 0.0 END), 0) AS hint_usage_rate
            FROM history
            WHERE date >= @cutoff
            """;
        AddParam(cmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var totalCompletions = reader.GetInt32(0);
        var uniquePlayers = reader.GetInt32(1);
        var avgTime = Math.Round(Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture), 1);
        var bestTime = Math.Round(Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture), 1);
        var hintUsageRate = Math.Round(Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture), 3);
        await reader.CloseAsync();

        // Registered users (aliases)
        await using var regCmd = conn.CreateCommand();
        regCmd.CommandText = "SELECT COUNT(*) FROM user_aliases";
        var registeredUsers = Convert.ToInt32(await regCmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);

        // Today's activity
        await using var todayCmd = conn.CreateCommand();
        todayCmd.CommandText = "SELECT COUNT(*), COUNT(DISTINCT name) FROM history WHERE date = @today";
        AddParam(todayCmd, "@today", today);
        await using var todayReader = await todayCmd.ExecuteReaderAsync();
        await todayReader.ReadAsync();
        var completionsToday = todayReader.GetInt32(0);
        var activeToday = todayReader.GetInt32(1);
        await todayReader.CloseAsync();

        // Per puzzle size
        await using var sizeCmd = conn.CreateCommand();
        sizeCmd.CommandText = """
            SELECT puzzle_size, COUNT(*), COALESCE(AVG(time), 0)
            FROM history
            WHERE date >= @cutoff AND puzzle_size IS NOT NULL AND puzzle_size != ''
            GROUP BY puzzle_size
            ORDER BY puzzle_size
            """;
        AddParam(sizeCmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var perSize = new Dictionary<string, SizeCompletions>();
        await using var sizeReader = await sizeCmd.ExecuteReaderAsync();
        while (await sizeReader.ReadAsync())
        {
            perSize[sizeReader.GetString(0)] = new SizeCompletions(
                sizeReader.GetInt32(1), Math.Round(Convert.ToDouble(sizeReader.GetValue(2), CultureInfo.InvariantCulture), 1));
        }

        return new AnalyticsSummary(
            TotalCompletions: totalCompletions,
            UniquePlayers: uniquePlayers,
            RegisteredUsers: registeredUsers,
            CompletionsToday: completionsToday,
            ActiveToday: activeToday,
            AverageTime: avgTime,
            BestTime: bestTime,
            HintUsageRate: hintUsageRate,
            PerSize: perSize
        );
    }

    public async Task<List<DailyAnalytics>> GetDailyAnalyticsAsync(int days)
    {
        var today = _timeProvider.GetSwedishDate();
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
                MIN(time)            AS best_time
            FROM history
            WHERE date >= @earliest AND date <= @today
            GROUP BY date
            ORDER BY date DESC
            """;
        AddParam(cmd, "@earliest", earliest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParam(cmd, "@today", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var result = new List<DailyAnalytics>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new DailyAnalytics(
                Date: reader.GetString(0),
                Completions: reader.GetInt32(1),
                UniquePlayers: reader.GetInt32(2),
                AverageTime: Math.Round(reader.GetDouble(3), 1),
                BestTime: Math.Round(reader.GetDouble(4), 1)
            ));
        }

        return result;
    }

    public async Task<List<TopPlayer>> GetTopPlayersAsync(int limit)
    {
        await using var conn = await OpenConnectionAsync();

        // Load alias lookup
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var aliasCmd = conn.CreateCommand())
        {
            aliasCmd.CommandText = "SELECT user_id, alias FROM user_aliases";
            await using var aliasReader = await aliasCmd.ExecuteReaderAsync();
            while (await aliasReader.ReadAsync())
                aliases[aliasReader.GetString(0)] = aliasReader.GetString(1);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              SELECT TOP (@limit)
                  COALESCE(user_id, name) AS player_key,
                  MAX(name)  AS name,
                  MAX(user_id) AS user_id,
                  COUNT(*)  AS games_played,
                  AVG(time) AS avg_time,
                  MIN(time) AS best_time
              FROM history
              WHERE date >= @cutoff
              GROUP BY COALESCE(user_id, name)
              ORDER BY games_played DESC, avg_time ASC
              """
            : """
              SELECT
                  COALESCE(user_id, name) AS player_key,
                  MAX(name)  AS name,
                  MAX(user_id) AS user_id,
                  COUNT(*)  AS games_played,
                  AVG(time) AS avg_time,
                  MIN(time) AS best_time
              FROM history
              WHERE date >= @cutoff
              GROUP BY COALESCE(user_id, name)
              ORDER BY games_played DESC, avg_time ASC
              LIMIT @limit
              """;
        AddParam(cmd, "@cutoff", HistoryCutoffDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddParam(cmd, "@limit", limit);

        var result = new List<TopPlayer>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var rawName = reader.GetString(1);
            var userId = reader.IsDBNull(2) ? null : reader.GetString(2);
            var verified = userId is not null;
            string? alias = null;
            if (userId is not null)
                aliases.TryGetValue(userId, out alias);
            result.Add(new TopPlayer(
                DisplayName: alias ?? rawName,
                RawName: rawName,
                Verified: verified,
                GamesPlayed: reader.GetInt32(3),
                AverageTime: Math.Round(reader.GetDouble(4), 1),
                BestTime: Math.Round(reader.GetDouble(5), 1)
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
            SELECT id, status, from_user_id FROM friend_requests
            WHERE (from_user_id = @from AND to_user_id = @to)
               OR (from_user_id = @to AND to_user_id = @from)
            """;
        AddParam(check, "@from", fromUserId);
        AddParam(check, "@to", toUserId);

        await using var reader = await check.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var existingId = reader.GetString(0);
            var status = reader.GetString(1);
            var existingFrom = reader.GetString(2);
            await reader.CloseAsync();

            // Auto-accept: B sends request to A while A→B is already pending
            if (status == "pending" && existingFrom == toUserId)
            {
                await using var accept = conn.CreateCommand();
                accept.Transaction = tx;
                accept.CommandText = "UPDATE friend_requests SET status = 'accepted' WHERE id = @id AND status = 'pending'";
                AddParam(accept, "@id", existingId);
                await accept.ExecuteNonQueryAsync();
                await tx.CommitAsync();
                return (true, string.Empty);
            }

            await tx.RollbackAsync();
            return status switch
            {
                "accepted" => (false, "Ni är redan vänner"),
                "pending" => (false, "En vänförfrågan finns redan"),
                "declined" => (false, "En vänförfrågan har redan avböjts"),
                _ => (false, "En vänförfrågan finns redan")
            };
        }
        await reader.CloseAsync();

        await using var countCheck = conn.CreateCommand();
        countCheck.Transaction = tx;
        countCheck.CommandText = "SELECT COUNT(*) FROM friend_requests WHERE from_user_id = @from AND status = 'pending'";
        AddParam(countCheck, "@from", fromUserId);
        var pendingCount = Convert.ToInt64(await countCheck.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
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

    public async Task<List<FriendsLeaderboardEntry>> GetFriendsLeaderboardAsync(string userId, string date, string? puzzleHash = null)
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

        var hashFilter = string.IsNullOrWhiteSpace(puzzleHash) ? "" : " AND h.puzzle_hash = @hash";
        cmd.CommandText = $"""
            SELECT a.alias, h.time, h.timestamp, h.puzzle_hash, h.hints_used, h.word_hints_used
            FROM history h
            JOIN user_aliases a ON a.user_id = h.user_id
            WHERE h.date = @date AND h.user_id IN ({string.Join(',', paramNames)}){hashFilter}
            ORDER BY h.time
            """;
        AddParam(cmd, "@date", date);
        if (!string.IsNullOrWhiteSpace(puzzleHash))
            AddParam(cmd, "@hash", puzzleHash);

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

    public async Task<(bool Success, string Error)> CreateChallengeAsync(string fromUserId, string friendRequestId, string date)
    {
        await using var conn = await OpenConnectionAsync();

        string? toUserId = null;
        await using (var friendshipCmd = conn.CreateCommand())
        {
            friendshipCmd.CommandText = """
                SELECT from_user_id, to_user_id
                FROM friend_requests
                WHERE id = @id AND status = 'accepted'
                  AND (from_user_id = @me OR to_user_id = @me)
                """;
            AddParam(friendshipCmd, "@id", friendRequestId);
            AddParam(friendshipCmd, "@me", fromUserId);

            await using var reader = await friendshipCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return (false, "Vänskapen hittades inte");

            var first = reader.GetString(0);
            var second = reader.GetString(1);
            toUserId = string.Equals(first, fromUserId, StringComparison.Ordinal) ? second : first;
        }

        if (string.IsNullOrWhiteSpace(toUserId))
            return (false, "Kunde inte skapa utmaning");

        await using (var dedup = conn.CreateCommand())
        {
            dedup.CommandText = """
                SELECT COUNT(1)
                FROM friend_challenges
                WHERE friendship_id = @fid
                  AND from_user_id = @from
                  AND to_user_id = @to
                  AND challenge_date = @date
                  AND status = 'pending'
                """;
            AddParam(dedup, "@fid", friendRequestId);
            AddParam(dedup, "@from", fromUserId);
            AddParam(dedup, "@to", toUserId);
            AddParam(dedup, "@date", date);

            var exists = Convert.ToInt64(await dedup.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
            if (exists)
                return (false, "Du har redan en väntande utmaning till den här vännen för datumet");
        }

        await using var insert = conn.CreateCommand();
        insert.CommandText = """
            INSERT INTO friend_challenges (id, friendship_id, from_user_id, to_user_id, challenge_date, status, created_at, responded_at)
            VALUES (@id, @fid, @from, @to, @date, 'pending', @created, NULL)
            """;
        AddParam(insert, "@id", Guid.NewGuid().ToString("N"));
        AddParam(insert, "@fid", friendRequestId);
        AddParam(insert, "@from", fromUserId);
        AddParam(insert, "@to", toUserId);
        AddParam(insert, "@date", date);
        AddParam(insert, "@created", _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());

        await insert.ExecuteNonQueryAsync();
        return (true, string.Empty);
    }

    public async Task<List<FriendChallengeInfo>> GetChallengesAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              SELECT c.id,
                     CASE WHEN c.from_user_id = @uid THEN toAlias.alias ELSE fromAlias.alias END AS friend_alias,
                     c.challenge_date,
                     c.status,
                     CASE WHEN c.to_user_id = @uid THEN 'incoming' ELSE 'outgoing' END AS direction,
                     c.created_at,
                     c.responded_at
              FROM friend_challenges c
              LEFT JOIN user_aliases fromAlias ON fromAlias.user_id = c.from_user_id
              LEFT JOIN user_aliases toAlias ON toAlias.user_id = c.to_user_id
              WHERE c.from_user_id = @uid OR c.to_user_id = @uid
              ORDER BY c.created_at DESC
              """
            : """
              SELECT c.id,
                     CASE WHEN c.from_user_id = @uid THEN toAlias.alias ELSE fromAlias.alias END AS friend_alias,
                     c.challenge_date,
                     c.status,
                     CASE WHEN c.to_user_id = @uid THEN 'incoming' ELSE 'outgoing' END AS direction,
                     c.created_at,
                     c.responded_at
              FROM friend_challenges c
              LEFT JOIN user_aliases fromAlias ON fromAlias.user_id = c.from_user_id
              LEFT JOIN user_aliases toAlias ON toAlias.user_id = c.to_user_id
              WHERE c.from_user_id = @uid OR c.to_user_id = @uid
              ORDER BY c.created_at DESC
              """;
        AddParam(cmd, "@uid", userId);

        var list = new List<FriendChallengeInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new FriendChallengeInfo(
                Id: reader.GetString(0),
                FriendAlias: reader.IsDBNull(1) ? "Okänd" : reader.GetString(1),
                Date: reader.GetString(2),
                Status: reader.GetString(3),
                Direction: reader.GetString(4),
                CreatedAt: reader.GetInt64(5),
                RespondedAt: reader.IsDBNull(6) ? null : reader.GetInt64(6)
            ));
        }

        return list;
    }

    public async Task<bool> RespondToChallengeAsync(string challengeId, string userId, bool accepted)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE friend_challenges
            SET status = @status,
                responded_at = @respondedAt
            WHERE id = @id
              AND to_user_id = @uid
              AND status = 'pending'
            """;
        AddParam(cmd, "@status", accepted ? "accepted" : "declined");
        AddParam(cmd, "@respondedAt", _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        AddParam(cmd, "@id", challengeId);
        AddParam(cmd, "@uid", userId);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // ── Admin grants ──

    public async Task<bool> IsAdminAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM admin_grants WHERE user_id = @userId";
        AddParam(cmd, "@userId", userId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    public async Task GrantAdminAsync(string userId, string grantedByUserId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              IF NOT EXISTS (SELECT 1 FROM admin_grants WHERE user_id = @userId)
                  INSERT INTO admin_grants (user_id, granted_by, granted_at) VALUES (@userId, @grantedBy, @now)
              """
            : "INSERT OR IGNORE INTO admin_grants (user_id, granted_by, granted_at) VALUES (@userId, @grantedBy, @now)";
        AddParam(cmd, "@userId", userId);
        AddParam(cmd, "@grantedBy", grantedByUserId);
        AddParam(cmd, "@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RevokeAdminAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM admin_grants WHERE user_id = @userId";
        AddParam(cmd, "@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AdminGrantInfo>> ListGrantedAdminsAsync()
    {
        await using var conn = await OpenConnectionAsync();

        // Load alias lookup in one pass
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using (var aliasCmd = conn.CreateCommand())
        {
            aliasCmd.CommandText = "SELECT user_id, alias FROM user_aliases";
            await using var aliasReader = await aliasCmd.ExecuteReaderAsync();
            while (await aliasReader.ReadAsync())
                aliases[aliasReader.GetString(0)] = aliasReader.GetString(1);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT user_id, granted_by, granted_at FROM admin_grants ORDER BY granted_at DESC";

        var result = new List<AdminGrantInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var uid = reader.GetString(0);
            var grantedBy = reader.IsDBNull(1) ? null : reader.GetString(1);
            var grantedAt = reader.GetInt64(2);
            aliases.TryGetValue(uid, out var alias);
            string? grantedByAlias = null;
            if (grantedBy is not null) aliases.TryGetValue(grantedBy, out grantedByAlias);
            result.Add(new AdminGrantInfo(uid, alias, grantedAt, grantedByAlias));
        }
        return result;
    }

    // ── Clue flags ──

    public async Task<string> CreateClueFlagAsync(ClueFlagCreateRequest request, string? createdByUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO clue_flags (
                id, word, current_clue, suggested_clue, reason, status,
                created_at, created_by, reviewed_at, reviewed_by,
                updated_clue, puzzle_date, puzzle_size, puzzle_hash, admin_note)
            VALUES (
                @id, @word, @currentClue, @suggestedClue, @reason, 'pending',
                @createdAt, @createdBy, NULL, NULL,
                NULL, @puzzleDate, @puzzleSize, @puzzleHash, NULL)
            """;
        AddParam(cmd, "@id", id);
        AddParam(cmd, "@word", request.Word);
        AddParam(cmd, "@currentClue", request.CurrentClue);
        AddParam(cmd, "@suggestedClue", (object?)request.SuggestedClue ?? DBNull.Value);
        AddParam(cmd, "@reason", (object?)request.Reason ?? DBNull.Value);
        AddParam(cmd, "@createdAt", now);
        AddParam(cmd, "@createdBy", (object?)createdByUserId ?? DBNull.Value);
        AddParam(cmd, "@puzzleDate", (object?)request.PuzzleDate ?? DBNull.Value);
        AddParam(cmd, "@puzzleSize", (object?)request.PuzzleSize ?? DBNull.Value);
        AddParam(cmd, "@puzzleHash", (object?)request.PuzzleHash ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        return id;
    }

    public async Task<List<ClueFlagInfo>> ListPendingClueFlagsAsync(int limit)
    {
        var clamped = Math.Clamp(limit, 1, 200);

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _useSqlServer
            ? """
              SELECT TOP (@limit)
                  id, word, current_clue, suggested_clue, reason, status,
                  created_at, reviewed_at, updated_clue,
                  puzzle_date, puzzle_size, puzzle_hash, admin_note
              FROM clue_flags
              WHERE status = 'pending'
              ORDER BY created_at DESC
              """
            : """
              SELECT
                  id, word, current_clue, suggested_clue, reason, status,
                  created_at, reviewed_at, updated_clue,
                  puzzle_date, puzzle_size, puzzle_hash, admin_note
              FROM clue_flags
              WHERE status = 'pending'
              ORDER BY created_at DESC
              LIMIT @limit
              """;
        AddParam(cmd, "@limit", clamped);

        return await ReadClueFlagsAsync(cmd);
    }

    public async Task<ClueFlagInfo?> GetClueFlagAsync(string id)
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                id, word, current_clue, suggested_clue, reason, status,
                created_at, reviewed_at, updated_clue,
                puzzle_date, puzzle_size, puzzle_hash, admin_note
            FROM clue_flags
            WHERE id = @id
            """;
        AddParam(cmd, "@id", id);

        var items = await ReadClueFlagsAsync(cmd);
        return items.FirstOrDefault();
    }

    public async Task<bool> ResolveClueFlagAsync(string id, string status, string? updatedClue, string? adminNote, string resolvedByUserId)
    {
        var normalizedStatus = status.Trim().ToLowerInvariant();
        if (normalizedStatus is not ("approved" or "rejected"))
            throw new ArgumentException("Invalid clue flag status", nameof(status));

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE clue_flags
            SET status = @status,
                reviewed_at = @reviewedAt,
                reviewed_by = @reviewedBy,
                updated_clue = @updatedClue,
                admin_note = @adminNote
            WHERE id = @id
              AND status = 'pending'
            """;
        AddParam(cmd, "@status", normalizedStatus);
        AddParam(cmd, "@reviewedAt", _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        AddParam(cmd, "@reviewedBy", resolvedByUserId);
        AddParam(cmd, "@updatedClue", (object?)updatedClue ?? DBNull.Value);
        AddParam(cmd, "@adminNote", (object?)adminNote ?? DBNull.Value);
        AddParam(cmd, "@id", id);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static async Task<List<ClueFlagInfo>> ReadClueFlagsAsync(DbCommand cmd)
    {
        var result = new List<ClueFlagInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ClueFlagInfo(
                Id: reader.GetString(0),
                Word: reader.GetString(1),
                CurrentClue: reader.GetString(2),
                SuggestedClue: reader.IsDBNull(3) ? null : reader.GetString(3),
                Reason: reader.IsDBNull(4) ? null : reader.GetString(4),
                Status: reader.GetString(5),
                CreatedAt: reader.GetInt64(6),
                ReviewedAt: reader.IsDBNull(7) ? null : reader.GetInt64(7),
                UpdatedClue: reader.IsDBNull(8) ? null : reader.GetString(8),
                PuzzleDate: reader.IsDBNull(9) ? null : reader.GetString(9),
                PuzzleSize: reader.IsDBNull(10) ? null : reader.GetString(10),
                PuzzleHash: reader.IsDBNull(11) ? null : reader.GetString(11),
                AdminNote: reader.IsDBNull(12) ? null : reader.GetString(12)
            ));
        }

        return result;
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
        OpenSqlWithRetry(conn);

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

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_scores_dedup')
            CREATE INDEX idx_scores_dedup ON scores (leaderboard_key, name, time);

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

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_history_dedup')
            CREATE INDEX idx_history_dedup ON history (date, name, time);

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

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'friend_challenges')
            CREATE TABLE friend_challenges (
                id              NVARCHAR(50) NOT NULL PRIMARY KEY,
                friendship_id   NVARCHAR(50) NOT NULL,
                from_user_id    NVARCHAR(200) NOT NULL,
                to_user_id      NVARCHAR(200) NOT NULL,
                challenge_date  NVARCHAR(10) NOT NULL,
                status          NVARCHAR(20) NOT NULL DEFAULT 'pending',
                created_at      BIGINT NOT NULL,
                responded_at    BIGINT NULL
            );

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_friend_challenges_to_status')
            CREATE INDEX idx_friend_challenges_to_status ON friend_challenges (to_user_id, status);

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_friend_challenges_from_date')
            CREATE INDEX idx_friend_challenges_from_date ON friend_challenges (from_user_id, challenge_date);

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'admin_grants')
            CREATE TABLE admin_grants (
                user_id     NVARCHAR(200) NOT NULL PRIMARY KEY,
                granted_by  NVARCHAR(200) NULL,
                granted_at  BIGINT NOT NULL
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'clue_flags')
            CREATE TABLE clue_flags (
                id              NVARCHAR(50) NOT NULL PRIMARY KEY,
                word            NVARCHAR(64) NOT NULL,
                current_clue    NVARCHAR(500) NOT NULL,
                suggested_clue  NVARCHAR(500) NULL,
                reason          NVARCHAR(1000) NULL,
                status          NVARCHAR(20) NOT NULL,
                created_at      BIGINT NOT NULL,
                created_by      NVARCHAR(200) NULL,
                reviewed_at     BIGINT NULL,
                reviewed_by     NVARCHAR(200) NULL,
                updated_clue    NVARCHAR(500) NULL,
                puzzle_date     NVARCHAR(10) NULL,
                puzzle_size     NVARCHAR(20) NULL,
                puzzle_hash     NVARCHAR(100) NULL,
                admin_note      NVARCHAR(1000) NULL
            );

            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_clue_flags_status_created')
            CREATE INDEX idx_clue_flags_status_created ON clue_flags (status, created_at DESC);
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'idx_clue_flags_created_by')
            CREATE INDEX idx_clue_flags_created_by ON clue_flags (created_by);
            """;
        cmd.ExecuteNonQuery();
        _logger.LogInformation("Azure SQL database initialised");
    }

    private void InitialiseSqliteDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();

        // Attempt WAL mode — preferred but not supported on all file systems
        // (e.g. Azure Files SMB lacks mmap support required by WAL).
        using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode = WAL;";
        var result = walCmd.ExecuteScalar()?.ToString();
        if (string.Equals(result, "wal", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("SQLite journal mode set to WAL");
            using var syncCmd = conn.CreateCommand();
            syncCmd.CommandText = "PRAGMA synchronous = NORMAL;";
            syncCmd.ExecuteNonQuery();
        }
        else
        {
            _logger.LogWarning(
                "SQLite WAL mode not available (got '{Result}'). " +
                "Falling back to default journal mode with full synchronous writes. " +
                "This is expected on Azure Files SMB mounts.",
                result);
        }

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
            CREATE INDEX IF NOT EXISTS idx_scores_dedup ON scores (leaderboard_key, name, time);

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
            CREATE INDEX IF NOT EXISTS idx_history_dedup ON history (date, name, time);
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

            CREATE TABLE IF NOT EXISTS friend_challenges (
                id              TEXT NOT NULL PRIMARY KEY,
                friendship_id    TEXT NOT NULL,
                from_user_id    TEXT NOT NULL,
                to_user_id      TEXT NOT NULL,
                challenge_date  TEXT NOT NULL,
                status          TEXT NOT NULL DEFAULT 'pending',
                created_at      INTEGER NOT NULL,
                responded_at    INTEGER
            );
            CREATE INDEX IF NOT EXISTS idx_friend_challenges_to_status ON friend_challenges (to_user_id, status);
            CREATE INDEX IF NOT EXISTS idx_friend_challenges_from_date ON friend_challenges (from_user_id, challenge_date);

            CREATE TABLE IF NOT EXISTS admin_grants (
                user_id     TEXT NOT NULL PRIMARY KEY,
                granted_by  TEXT,
                granted_at  INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS clue_flags (
                id              TEXT NOT NULL PRIMARY KEY,
                word            TEXT NOT NULL,
                current_clue    TEXT NOT NULL,
                suggested_clue  TEXT,
                reason          TEXT,
                status          TEXT NOT NULL DEFAULT 'pending',
                created_at      INTEGER NOT NULL,
                created_by      TEXT,
                reviewed_at     INTEGER,
                reviewed_by     TEXT,
                updated_clue    TEXT,
                puzzle_date     TEXT,
                puzzle_size     TEXT,
                puzzle_hash     TEXT,
                admin_note      TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_clue_flags_status_created ON clue_flags (status, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_clue_flags_created_by ON clue_flags (created_by);
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
        if (_useSqlServer)
        {
            var sqlConn = new SqlConnection(_connectionString);
            await OpenSqlWithRetryAsync(sqlConn);
            return sqlConn;
        }

        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync();

        return conn;
    }

    // Transient SQL error classification lives in TransientSqlErrorClassifier
    // so the same set is shared with TransientDbExceptionHandler (which maps
    // unhandled transient SqlExceptions to HTTP 503 problem+json).
    private static bool IsTransient(SqlException ex) => TransientSqlErrorClassifier.IsTransient(ex);

    // 8 attempts with exponential backoff capped at 15s totals ~60s of waiting
    // (1+2+4+8+15+15+15s) — enough to cover an Azure SQL serverless cold-start
    // resume from auto-pause (typically 30–60s).
    private const int OpenSqlMaxAttempts = 8;

    private void OpenSqlWithRetry(SqlConnection conn)
    {
        const int maxAttempts = OpenSqlMaxAttempts;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                conn.Open();
                return;
            }
            catch (SqlException ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                var delayMs = Math.Min(15_000, 1000 * (1 << (attempt - 1)));
                _logger.LogWarning(ex,
                    "Transient SQL error opening connection (attempt {Attempt}/{Max}, error {Number}). Retrying in {Delay} ms.",
                    attempt, maxAttempts, ex.Number, delayMs);
                Thread.Sleep(delayMs);
            }
        }
    }

    private async Task OpenSqlWithRetryAsync(SqlConnection conn)
    {
        const int maxAttempts = OpenSqlMaxAttempts;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await conn.OpenAsync();
                return;
            }
            catch (SqlException ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                var delayMs = Math.Min(15_000, 1000 * (1 << (attempt - 1)));
                _logger.LogWarning(ex,
                    "Transient SQL error opening connection (attempt {Attempt}/{Max}, error {Number}). Retrying in {Delay} ms.",
                    attempt, maxAttempts, ex.Number, delayMs);
                await Task.Delay(delayMs);
            }
        }
    }

    private static async Task<List<ScoreRecord>> GetScoresForKeyAsync(DbConnection conn, DbTransaction? tx, string leaderboardKey)
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

    private static void AddParam(DbCommand cmd, int index, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = $"@p{index}";
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // ── GDPR: Data export ──

    public async Task<UserDataExport> ExportUserDataAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();

        // Alias
        string? alias = null;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT alias FROM user_aliases WHERE user_id = @uid";
            AddParam(cmd, "@uid", userId);
            alias = (await cmd.ExecuteScalarAsync()) as string;
        }

        // History
        var history = new List<UserSolveRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT date, time, puzzle_size, hints_used, word_hints_used FROM history WHERE user_id = @uid ORDER BY date DESC";
            AddParam(cmd, "@uid", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                history.Add(new UserSolveRecord(reader.GetString(0), reader.GetDouble(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4)));
        }

        // Scores
        var scores = new List<UserScoreExport>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT leaderboard_key, name, time, timestamp FROM scores WHERE user_id = @uid ORDER BY leaderboard_key";
            AddParam(cmd, "@uid", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                scores.Add(new UserScoreExport(reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        }

        // Friends
        var friends = await GetFriendsAsync(userId);

        return new UserDataExport(userId, alias, history, scores, [.. friends.Select(f => f.Alias)]);
    }

    // ── GDPR: Account deletion ──

    public async Task DeleteUserDataAsync(string userId)
    {
        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            // Anonymise history records (keep aggregate data, remove identity)
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE history SET user_id = NULL, name = 'Raderad' WHERE user_id = @uid";
                AddParam(cmd, "@uid", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Anonymise score records
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE scores SET user_id = NULL, name = 'Raderad' WHERE user_id = @uid";
                AddParam(cmd, "@uid", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Delete friend requests
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM friend_requests WHERE from_user_id = @uid OR to_user_id = @uid";
                AddParam(cmd, "@uid", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Delete alias
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM user_aliases WHERE user_id = @uid";
                AddParam(cmd, "@uid", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            _logger.LogInformation("Deleted all data for user {UserId}", userId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static bool IsUniqueConstraintViolation(DbException ex) =>
        ex is SqliteException { SqliteErrorCode: 19 } // SQLITE_CONSTRAINT
        || ex is SqlException { Number: 2601 or 2627 }; // SQL Server unique index / PK violation
}
