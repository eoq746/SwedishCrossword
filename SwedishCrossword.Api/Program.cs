using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using SwedishCrossword.Api;
using SwedishCrossword.Services;

var builder = WebApplication.CreateBuilder(args);

// Limit request body size globally (no endpoint needs more than 50 KB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 100 * 1024;
});

// Register domain services
builder.Services.AddSingleton<SwedishDictionary>();
builder.Services.AddSingleton<GridValidator>();
builder.Services.AddSingleton<ClueGenerator>();
builder.Services.AddSingleton<CrosswordGenerator>();
builder.Services.AddSingleton<PrintService>();
builder.Services.AddSingleton<LeaderboardStore>();
builder.Services.AddSingleton<SubmissionTokenService>();

// Background service: pre-generates today's puzzle at startup so the first visitor never waits
builder.Services.AddHostedService<PuzzleWarmupService>();

// Forwarded headers — required behind Azure App Service reverse proxy for correct client IPs
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Health checks
builder.Services.AddHealthChecks();

// Output caching — avoids redundant disk reads for puzzle endpoints
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("puzzle-today", p => p.Expire(TimeSpan.FromMinutes(5)));
    options.AddPolicy("puzzle-archive", p => p.Expire(TimeSpan.FromHours(1)));
    options.AddPolicy("puzzle-dates", p => p.Expire(TimeSpan.FromMinutes(10)));
});

// Response compression — Brotli + Gzip for JSON and static assets
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// OpenAPI documentation
builder.Services.AddOpenApi();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global per-IP rate limit for all endpoints
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 200,
                QueueLimit = 0
            }));

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

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(err => err.Run(async ctx =>
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"error\":\"An unexpected error occurred.\"}");
    }));
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRateLimiter();
app.UseResponseCompression();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var isProduction = !app.Environment.IsDevelopment();

// Security headers
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (isProduction)
        headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

// ---------------------------------------------------------------------------
// Puzzle endpoints
// ---------------------------------------------------------------------------

app.MapGet("/api/puzzle/today", async (string? size, SubmissionTokenService tokenService) =>
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var datePart = $"puzzle-{today:yyyy-MM-dd}";
    var suffix = string.Equals(size, "small", StringComparison.OrdinalIgnoreCase) ? "-small" : "";
    var todayFile = Path.Combine(puzzlePath, $"{datePart}{suffix}.json");

    if (File.Exists(todayFile))
    {
        var json = await File.ReadAllTextAsync(todayFile);
        return Results.Content(tokenService.InjectToken(json, today), "application/json");
    }

    // Fall back to standard puzzle when small variant is not yet available
    if (suffix.Length > 0)
    {
        var fallback = Path.Combine(puzzlePath, $"{datePart}.json");
        if (File.Exists(fallback))
        {
            var json = await File.ReadAllTextAsync(fallback);
            return Results.Content(tokenService.InjectToken(json, today), "application/json");
        }
    }

    // Puzzle is generated by PuzzleWarmupService at startup; not ready yet
    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).CacheOutput("puzzle-today");


app.MapGet("/api/puzzle/{date}", async (string date, string? size, SubmissionTokenService tokenService) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
        return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

    // Only allow access to today's or past puzzles
    if (parsedDate > DateOnly.FromDateTime(DateTime.UtcNow))
        return Results.NotFound(new { error = "No puzzle available for the requested date." });

    var suffix = string.Equals(size, "small", StringComparison.OrdinalIgnoreCase) ? "-small" : "";
    var puzzleFile = Path.Combine(puzzlePath, $"puzzle-{parsedDate:yyyy-MM-dd}{suffix}.json");

    if (File.Exists(puzzleFile))
    {
        var json = await File.ReadAllTextAsync(puzzleFile);
        return Results.Content(tokenService.InjectToken(json, parsedDate), "application/json");
    }

    // Fall back to standard puzzle when small variant is not available
    if (suffix.Length > 0)
    {
        var fallback = Path.Combine(puzzlePath, $"puzzle-{parsedDate:yyyy-MM-dd}.json");
        if (File.Exists(fallback))
        {
            var json = await File.ReadAllTextAsync(fallback);
            return Results.Content(tokenService.InjectToken(json, parsedDate), "application/json");
        }
    }

    return Results.NotFound(new { error = "No puzzle available for the requested date." });
}).CacheOutput("puzzle-archive");

// ---------------------------------------------------------------------------
// Leaderboard endpoints
// ---------------------------------------------------------------------------

app.MapGet("/api/leaderboard", async (LeaderboardStore store) =>
{
    var data = await store.GetCurrentAsync();
    return Results.Content(data, "application/json");
});

// ---------------------------------------------------------------------------
// Score submission (token-validated, server-managed)
// ---------------------------------------------------------------------------

app.MapPost("/api/scores", async (ScoreSubmissionRequest body, SubmissionTokenService tokenService, LeaderboardStore store) =>
{
    var name = LeaderboardStore.SanitiseName(body.Name);
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "Invalid name" });

    if (body.Time < 0 || body.Time > 86400)
        return Results.BadRequest(new { error = "Invalid time" });

    if (string.IsNullOrWhiteSpace(body.Token))
        return Results.Json(new { error = "Missing submission token" }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(body.PuzzleHash))
        return Results.BadRequest(new { error = "Missing puzzle hash" });

    if (string.IsNullOrWhiteSpace(body.Date) || !LeaderboardStore.DatePattern.IsMatch(body.Date))
        return Results.BadRequest(new { error = "Invalid date format" });

    var validation = tokenService.Validate(body.Token, body.PuzzleHash, body.Time);
    if (!validation.IsValid)
        return Results.Json(new { error = validation.Error }, statusCode: 403);

    var leaderboardKey = $"{body.Date}-{body.PuzzleHash}";
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var entry = new ScoreRecord(name, body.Time, timestamp, body.PuzzleHash);
    var leaderboard = await store.AppendScoreAsync(leaderboardKey, entry);

    // Also archive to historical leaderboard
    await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Time, timestamp, body.PuzzleHash, body.PuzzleSize));

    return Results.Ok(new { success = true, leaderboard });
}).RequireRateLimiting("leaderboard-write");

app.MapPost("/api/leaderboard/history", async (LeaderboardHistoryRequest body, SubmissionTokenService tokenService, LeaderboardStore store) =>
{
    if (string.IsNullOrWhiteSpace(body.Token))
        return Results.Json(new { error = "Missing submission token" }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(body.Date) || !LeaderboardStore.DatePattern.IsMatch(body.Date))
        return Results.BadRequest(new { error = "Invalid date format" });

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", out var historyDate)
        || historyDate < today.AddDays(-90)
        || historyDate > today.AddDays(1))
        return Results.BadRequest(new { error = "Date out of range" });

    if (body.Entry is null || body.Entry.Time < 0 || body.Entry.Time > 86400)
        return Results.BadRequest(new { error = "Invalid entry" });

    if (string.IsNullOrWhiteSpace(body.Entry.PuzzleHash))
        return Results.BadRequest(new { error = "Missing puzzle hash" });

    var validation = tokenService.Validate(body.Token, body.Entry.PuzzleHash, body.Entry.Time);
    if (!validation.IsValid)
        return Results.Json(new { error = validation.Error }, statusCode: 403);

    var name = LeaderboardStore.SanitiseName(body.Entry.Name);
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "Invalid name" });

    await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Entry.Time, body.Entry.Timestamp, body.Entry.PuzzleHash, body.Entry.PuzzleSize));
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
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var dates = Directory.GetFiles(puzzlePath, "puzzle-*.json")
        .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-small"))
        .Select(f => Path.GetFileNameWithoutExtension(f).Replace("puzzle-", ""))
        .Where(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", out var parsed) && parsed <= today)
        .OrderDescending()
        .ToArray();

    return Results.Ok(dates);
}).CacheOutput("puzzle-dates");

// ---------------------------------------------------------------------------
// Stats
// ---------------------------------------------------------------------------

string[] availableDifficulties = ["easy", "medium", "hard", "small", "mobile"];

app.MapGet("/api/stats", (SwedishDictionary dictionary) =>
{
    return Results.Ok(new
    {
        wordCount = dictionary.WordCount,
        availableDifficulties
    });
});

app.MapHealthChecks("/api/health");

app.Run();

// ---------------------------------------------------------------------------
// Request / response models
// ---------------------------------------------------------------------------

record ScoreSubmissionRequest(string Token, string Name, double Time, string PuzzleHash, string Date, string? PuzzleSize = null);
record ScoreRecord(string Name, double Time, long? Timestamp, string? PuzzleHash);
record LeaderboardHistoryRequest(string Date, LeaderboardEntry Entry, string? Token = null);
record LeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null);
record HistoryRecord(string Name, double Time, long? Timestamp, string? PuzzleHash, string? PuzzleSize = null);

// ---------------------------------------------------------------------------
// Leaderboard file store
// ---------------------------------------------------------------------------

sealed class LeaderboardStore
{
    public static readonly Regex DatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

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

            // Prune entries older than 7 days
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7).ToString("yyyy-MM-dd");
            foreach (var key in allScores.Keys.ToList())
            {
                var dateMatch = Regex.Match(key, @"^(\d{4}-\d{2}-\d{2})");
                if (dateMatch.Success && string.Compare(dateMatch.Groups[1].Value, cutoff, StringComparison.Ordinal) < 0)
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

