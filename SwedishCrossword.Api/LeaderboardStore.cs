using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SwedishCrossword.Api;

sealed class LeaderboardStore
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

    private readonly string _dataDir;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LeaderboardStore(IConfiguration config)
    {
        var path = config["Storage:LeaderboardPath"];
        _dataDir = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "leaderboard")
            : path;
        Directory.CreateDirectory(_dataDir);
    }

    public static string SanitiseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sanitised = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return sanitised[..Math.Min(sanitised.Length, 30)];
    }

    // GET /leaderboard — return the current leaderboard JSON as-is
    public async Task<string> GetCurrentAsync()
    {
        var path = Path.Combine(_dataDir, "current.json");
        if (!File.Exists(path)) return "{}";
        return await File.ReadAllTextAsync(path);
    }

    // POST /api/scores — append a validated score to the leaderboard
    public async Task<List<ScoreRecord>> AppendScoreAsync(string leaderboardKey, ScoreRecord entry)
    {
        await _lock.WaitAsync();
        try
        {
            var path = Path.Combine(_dataDir, "current.json");
            var allScores = new Dictionary<string, List<ScoreRecord>>();

            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("scores", out var scores) && scores.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in scores.EnumerateObject())
                    {
                        var records = JsonSerializer.Deserialize<List<ScoreRecord>>(prop.Value.GetRawText(), JsonOptions);
                        if (records != null)
                            allScores[prop.Name] = records;
                    }
                }
            }

            if (!allScores.TryGetValue(leaderboardKey, out var list))
            {
                list = [];
                allScores[leaderboardKey] = list;
            }

            // Deduplicate
            var isDuplicate = list.Any(e =>
                e.Name == entry.Name && Math.Abs(e.Time - entry.Time) < 0.001 && e.Timestamp == entry.Timestamp);

            if (!isDuplicate)
            {
                list.Add(entry);
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                if (list.Count > 10)
                    allScores[leaderboardKey] = list = [.. list.Take(10)];
            }

            // Prune entries older than 7 days or before the history cutoff date
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
            if (cutoff < HistoryCutoffDate) cutoff = HistoryCutoffDate;
            var cutoffStr = cutoff.ToString("yyyy-MM-dd");
            foreach (var key in allScores.Keys.ToList())
            {
                var dateMatch = Regex.Match(key, @"^(\d{4}-\d{2}-\d{2})");
                if (dateMatch.Success && string.Compare(dateMatch.Groups[1].Value, cutoffStr, StringComparison.Ordinal) < 0)
                    allScores.Remove(key);
            }

            var output = JsonSerializer.Serialize(new { scores = allScores }, JsonOptions);
            await File.WriteAllTextAsync(path, output);

            return allScores.GetValueOrDefault(leaderboardKey) ?? [];
        }
        finally
        {
            _lock.Release();
        }
    }

    // POST /leaderboard/history — append a record for a specific date
    public async Task AppendHistoryAsync(string date, HistoryRecord record)
    {
        await _lock.WaitAsync();
        try
        {
            var path = GetHistoryPath(date);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var existing = new List<HistoryRecord>();
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                existing = JsonSerializer.Deserialize<List<HistoryRecord>>(json, JsonOptions) ?? [];
            }

            // Deduplicate
            var isDuplicate = existing.Any(e =>
                e.Name == record.Name && Math.Abs(e.Time - record.Time) < 0.001 && e.Timestamp == record.Timestamp);

            if (!isDuplicate)
            {
                existing.Add(record);

                // Keep top 10 per puzzle hash, capped at 50 total records
                var groups = existing.GroupBy(e => e.PuzzleHash ?? "_default");
                var trimmed = groups
                    .SelectMany(g => g.OrderBy(e => e.Time).Take(10))
                    .OrderBy(e => e.Time)
                    .Take(50)
                    .ToList();

                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(trimmed, JsonOptions));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // GET /leaderboard/history?days=N — return historical data
    public async Task<Dictionary<string, List<HistoryRecord>>> GetHistoryAsync(int days)
    {
        var result = new Dictionary<string, List<HistoryRecord>>();
        var today = DateTime.UtcNow.Date;

        for (var i = 0; i < days; i++)
        {
            var d = today.AddDays(-i);
            if (DateOnly.FromDateTime(d) < HistoryCutoffDate) break;

            var date = d.ToString("yyyy-MM-dd");
            var path = GetHistoryPath(date);
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                var records = JsonSerializer.Deserialize<List<HistoryRecord>>(json, JsonOptions);
                if (records is { Count: > 0 })
                    result[date] = records;
            }
        }

        return result;
    }

    private string GetHistoryPath(string date) =>
        Path.Combine(_dataDir, "history", $"{date}.json");
}
