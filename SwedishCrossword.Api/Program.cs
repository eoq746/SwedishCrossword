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

app.MapGet("/api/puzzle/today", async () =>
{
    var todayFile = Path.Combine(puzzlePath, $"puzzle-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.json");

    if (File.Exists(todayFile))
    {
        var json = await File.ReadAllTextAsync(todayFile);
        return Results.Content(json, "application/json");
    }

    // Puzzle is generated by PuzzleWarmupService at startup; not ready yet
    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
}).CacheOutput("puzzle-today");

app.MapGet("/api/puzzle/{date}", async (string date) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
        return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });

    // Only allow access to today's or past puzzles
    if (parsedDate > DateOnly.FromDateTime(DateTime.UtcNow))
        return Results.NotFound(new { error = "No puzzle available for the requested date." });

    var puzzleFile = Path.Combine(puzzlePath, $"puzzle-{parsedDate:yyyy-MM-dd}.json");

    if (File.Exists(puzzleFile))
    {
        var json = await File.ReadAllTextAsync(puzzleFile);
        return Results.Content(json, "application/json");
    }

    return Results.NotFound(new { error = "No puzzle available for the requested date." });
}).CacheOutput("puzzle-archive");

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
    if (request.ContentLength is null || request.ContentLength > 50 * 1024)
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

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", out var historyDate)
        || historyDate < today.AddDays(-90)
        || historyDate > today.AddDays(1))
        return Results.BadRequest(new { error = "Date out of range" });

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
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var dates = Directory.GetFiles(puzzlePath, "puzzle-*.json")
        .Select(f => Path.GetFileNameWithoutExtension(f).Replace("puzzle-", ""))
        .Where(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", out var parsed) && parsed <= today)
        .OrderDescending()
        .ToArray();

    return Results.Ok(dates);
}).CacheOutput("puzzle-dates");

// ---------------------------------------------------------------------------
// Stats
// ---------------------------------------------------------------------------

string[] availableDifficulties = ["easy", "medium", "hard", "small"];

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

record LeaderboardHistoryRequest(string Date, LeaderboardEntry Entry);
record LeaderboardEntry(string Name, double Time, long? Timestamp, string? PuzzleHash);
record HistoryRecord(string Name, double Time, long? Timestamp, string? PuzzleHash);

// ---------------------------------------------------------------------------
// Leaderboard file store (replaces Cloudflare KV)
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

