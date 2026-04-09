using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SwedishCrossword.Api;
using SwedishCrossword.Services;

var builder = WebApplication.CreateBuilder(args);

// Register domain services
builder.Services.AddSingleton<SwedishDictionary>();
builder.Services.AddSingleton<GridValidator>();
builder.Services.AddSingleton<ClueGenerator>();
builder.Services.AddSingleton<CrosswordGenerator>();
builder.Services.AddSingleton<PrintService>();
builder.Services.AddSingleton<LeaderboardStore>();

// Background service: pre-generates today's puzzle at startup so the first visitor never waits
builder.Services.AddHostedService<PuzzleWarmupService>();

// Health checks
builder.Services.AddHealthChecks();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("generate", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 5;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("leaderboard-write", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
        opt.QueueLimit = 0;
    });
});

// CORS — configurable via Cors:AllowedOrigins in appsettings (use ["*"] to allow all)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 } && !allowedOrigins.Contains("*"))
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Resolve configurable storage paths (override via env vars: Storage__PuzzlePath, Storage__LeaderboardPath)
var puzzlePath = app.Configuration["Storage:PuzzlePath"];
if (string.IsNullOrWhiteSpace(puzzlePath))
    puzzlePath = Path.Combine(AppContext.BaseDirectory, "puzzles");
Directory.CreateDirectory(puzzlePath);

app.UseCors();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

// ---------------------------------------------------------------------------
// Puzzle endpoints
// ---------------------------------------------------------------------------

app.MapGet("/api/puzzle/today", async (PrintService printService, CrosswordGenerator generator) =>
{
    var todayFile = Path.Combine(puzzlePath, $"puzzle-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.json");

    if (File.Exists(todayFile))
    {
        var json = await File.ReadAllTextAsync(todayFile);
        return Results.Content(json, "application/json");
    }

    var puzzle = await generator.GenerateAsync(CrosswordGenerationOptions.Hard);
    var content = printService.GenerateJsonForWeb(puzzle);

    await File.WriteAllTextAsync(todayFile, content);

    return Results.Content(content, "application/json");
});

app.MapGet("/api/puzzle/{date}", async (string date, PrintService printService, CrosswordGenerator generator) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
        return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

    var puzzleFile = Path.Combine(puzzlePath, $"puzzle-{parsedDate:yyyy-MM-dd}.json");

    if (File.Exists(puzzleFile))
    {
        var json = await File.ReadAllTextAsync(puzzleFile);
        return Results.Content(json, "application/json");
    }

    if (parsedDate < DateOnly.FromDateTime(DateTime.UtcNow))
        return Results.NotFound(new { error = "No puzzle available for the requested date." });

    var puzzle = await generator.GenerateAsync(CrosswordGenerationOptions.Hard);
    var content = printService.GenerateJsonForWeb(puzzle);

    await File.WriteAllTextAsync(puzzleFile, content);

    return Results.Content(content, "application/json");
});

app.MapPost("/api/puzzle/generate", async (GenerateRequest? request, PrintService printService, CrosswordGenerator generator) =>
{
    var options = (request?.Difficulty?.ToLowerInvariant()) switch
    {
        "easy" => CrosswordGenerationOptions.Easy,
        "medium" => CrosswordGenerationOptions.Medium,
        "hard" => CrosswordGenerationOptions.Hard,
        "small" => CrosswordGenerationOptions.Small,
        _ => CrosswordGenerationOptions.Hard
    };

    var puzzle = await generator.GenerateAsync(options);
    var json = printService.GenerateJsonForWeb(puzzle);
    return Results.Content(json, "application/json");
}).RequireRateLimiting("generate");

// ---------------------------------------------------------------------------
// Leaderboard endpoints (replaces Cloudflare Worker)
// ---------------------------------------------------------------------------

app.MapGet("/api/leaderboard", async (LeaderboardStore store) =>
{
    var data = await store.GetCurrentAsync();
    return Results.Content(data, "application/json");
});

app.MapPut("/api/leaderboard", async (HttpRequest request, LeaderboardStore store) =>
{
    if (request.ContentLength > 50 * 1024)
        return Results.StatusCode(413);

    using var doc = await JsonDocument.ParseAsync(request.Body);
    var root = doc.RootElement;

    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("scores", out var scores) || scores.ValueKind != JsonValueKind.Object)
        return Results.BadRequest(new { error = "Expected { scores: { ... } }" });

    if (scores.EnumerateObject().Count() > 30)
        return Results.BadRequest(new { error = "Too many leaderboard date keys" });

    await store.SaveCurrentAsync(root);
    return Results.Ok(new { success = true });
}).RequireRateLimiting("leaderboard-write");

app.MapPost("/api/leaderboard/history", async (LeaderboardHistoryRequest body, LeaderboardStore store) =>
{
    if (string.IsNullOrWhiteSpace(body.Date) || !LeaderboardStore.DatePattern.IsMatch(body.Date))
        return Results.BadRequest(new { error = "Invalid date format" });

    if (body.Entry is null || body.Entry.Time < 0 || body.Entry.Time > 86400)
        return Results.BadRequest(new { error = "Invalid entry" });

    var name = LeaderboardStore.SanitiseName(body.Entry.Name);
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "Invalid name" });

    await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Entry.Time, body.Entry.Timestamp, body.Entry.PuzzleHash));
    return Results.Ok(new { ok = true });
}).RequireRateLimiting("leaderboard-write");

app.MapGet("/api/leaderboard/history", async (int? days, LeaderboardStore store) =>
{
    var d = Math.Clamp(days ?? 30, 1, 90);
    var history = await store.GetHistoryAsync(d);
    return Results.Ok(history);
});

// ---------------------------------------------------------------------------
// Puzzle archive
// ---------------------------------------------------------------------------

app.MapGet("/api/puzzle/dates", () =>
{
    var dates = Directory.GetFiles(puzzlePath, "puzzle-*.json")
        .Select(f => Path.GetFileNameWithoutExtension(f).Replace("puzzle-", ""))
        .Where(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", out _))
        .OrderDescending()
        .ToArray();

    return Results.Ok(dates);
});

// ---------------------------------------------------------------------------
// Stats
// ---------------------------------------------------------------------------

app.MapGet("/api/stats", (SwedishDictionary dictionary) =>
{
    return Results.Ok(new
    {
        wordCount = dictionary.WordCount,
        availableDifficulties = new[] { "easy", "medium", "hard", "small" }
    });
});

app.MapHealthChecks("/api/health");

app.Run();

// ---------------------------------------------------------------------------
// Request / response models
// ---------------------------------------------------------------------------

record GenerateRequest(string? Difficulty);
record LeaderboardHistoryRequest(string Date, LeaderboardEntry Entry);
record LeaderboardEntry(string Name, double Time, string? Timestamp, string? PuzzleHash);
record HistoryRecord(string Name, double Time, string? Timestamp, string? PuzzleHash);

// ---------------------------------------------------------------------------
// Leaderboard file store (replaces Cloudflare KV)
// ---------------------------------------------------------------------------

sealed class LeaderboardStore
{
    public static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
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
        return new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim()[..Math.Min(name.Length, 30)];
    }

    // GET /leaderboard — return the current leaderboard JSON as-is
    public async Task<string> GetCurrentAsync()
    {
        var path = Path.Combine(_dataDir, "current.json");
        if (!File.Exists(path)) return "{}";
        return await File.ReadAllTextAsync(path);
    }

    // PUT /leaderboard — overwrite the current leaderboard
    public async Task SaveCurrentAsync(JsonElement data)
    {
        await _lock.WaitAsync();
        try
        {
            var path = Path.Combine(_dataDir, "current.json");
            await File.WriteAllTextAsync(path, data.GetRawText());
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

                // Keep top 10 per puzzle hash
                var groups = existing.GroupBy(e => e.PuzzleHash ?? "_default");
                var trimmed = groups.SelectMany(g => g.OrderBy(e => e.Time).Take(10)).ToList();

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
            var date = today.AddDays(-i).ToString("yyyy-MM-dd");
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

// Make Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
