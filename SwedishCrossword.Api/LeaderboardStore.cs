using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SwedishCrossword.Api;

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
            SELECT leaderboard_key, name, time, timestamp, puzzle_hash, hints_used, word_hints_used
            FROM scores
            ORDER BY leaderboard_key, time
            """;

        var allScores = new Dictionary<string, List<ScoreRecord>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var record = new ScoreRecord(
                Name: reader.GetString(1),
                Time: reader.GetDouble(2),
                Timestamp: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                PuzzleHash: reader.IsDBNull(4) ? null : reader.GetString(4),
                HintsUsed: reader.GetInt32(5),
                WordHintsUsed: reader.GetInt32(6)
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
                INSERT INTO scores (leaderboard_key, name, time, timestamp, puzzle_hash, hints_used, word_hints_used)
                VALUES (@key, @name, @time, @timestamp, @hash, @hints, @wordHints)
                """;
            insert.Parameters.AddWithValue("@key", leaderboardKey);
            insert.Parameters.AddWithValue("@name", entry.Name);
            insert.Parameters.AddWithValue("@time", entry.Time);
            insert.Parameters.AddWithValue("@timestamp", (object?)entry.Timestamp ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hash", (object?)entry.PuzzleHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hints", entry.HintsUsed);
            insert.Parameters.AddWithValue("@wordHints", entry.WordHintsUsed);
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
                INSERT INTO history (date, name, time, timestamp, puzzle_hash, puzzle_size, hints_used, word_hints_used)
                VALUES (@date, @name, @time, @timestamp, @hash, @size, @hints, @wordHints)
                """;
            insert.Parameters.AddWithValue("@date", date);
            insert.Parameters.AddWithValue("@name", record.Name);
            insert.Parameters.AddWithValue("@time", record.Time);
            insert.Parameters.AddWithValue("@timestamp", (object?)record.Timestamp ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hash", (object?)record.PuzzleHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("@size", (object?)record.PuzzleSize ?? DBNull.Value);
            insert.Parameters.AddWithValue("@hints", record.HintsUsed);
            insert.Parameters.AddWithValue("@wordHints", record.WordHintsUsed);
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
            SELECT date, name, time, timestamp, puzzle_hash, puzzle_size, hints_used, word_hints_used
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
                WordHintsUsed: reader.GetInt32(7)
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

    public void Dispose() => SqliteConnection.ClearAllPools();

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
                word_hints_used INTEGER NOT NULL DEFAULT 0
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
                word_hints_used INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_history_date
            ON history (date);
            """;
        create.ExecuteNonQuery();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var pragma = conn.CreateCommand();
        pragma.CommandText = """
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = NORMAL;
            """;
        await pragma.ExecuteNonQueryAsync();

        return conn;
    }

    private static async Task<List<ScoreRecord>> GetScoresForKeyAsync(SqliteConnection conn, string leaderboardKey)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name, time, timestamp, puzzle_hash, hints_used, word_hints_used
            FROM scores
            WHERE leaderboard_key = @key
            ORDER BY time
            """;
        cmd.Parameters.AddWithValue("@key", leaderboardKey);

        var list = new List<ScoreRecord>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ScoreRecord(
                Name: reader.GetString(0),
                Time: reader.GetDouble(1),
                Timestamp: reader.IsDBNull(2) ? null : reader.GetInt64(2),
                PuzzleHash: reader.IsDBNull(3) ? null : reader.GetString(3),
                HintsUsed: reader.GetInt32(4),
                WordHintsUsed: reader.GetInt32(5)
            ));
        }

        return list;
    }
}
