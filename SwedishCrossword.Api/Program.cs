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

    options.AddFixedWindowLimiter("puzzle-interact", opt =>
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
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self'; " +
        "font-src 'self'; " +
        "worker-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
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
    var entry = new ScoreRecord(name, body.Time, timestamp, body.PuzzleHash, body.HintsUsed, body.WordHintsUsed);
    var leaderboard = await store.AppendScoreAsync(leaderboardKey, entry);

    // Also archive to historical leaderboard (best-effort; don't fail the request)
    try
    {
        await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Time, timestamp, body.PuzzleHash, body.PuzzleSize, body.HintsUsed, body.WordHintsUsed));
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to archive history for {Date}/{Hash}", body.Date, body.PuzzleHash);
    }

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

    await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Entry.Time, body.Entry.Timestamp, body.Entry.PuzzleHash, body.Entry.PuzzleSize, body.Entry.HintsUsed, body.Entry.WordHintsUsed));
    return Results.Ok(new { ok = true });
}).RequireRateLimiting("leaderboard-write");

app.MapGet("/api/leaderboard/history", async (int? days, LeaderboardStore store) =>
{
    var d = Math.Clamp(days ?? 30, 1, 90);
    var history = await store.GetHistoryAsync(d);
    return Results.Ok(history);
});


// ---------------------------------------------------------------------------
// Puzzle answer validation & hints (server-side — answers stripped from client JSON)
// ---------------------------------------------------------------------------

// Helper to resolve the puzzle file path for a given date and optional size variant
string? ResolvePuzzleFile(string puzzleDate, string? size)
{
    var suffix = string.Equals(size, "small", StringComparison.OrdinalIgnoreCase) ? "-small" : "";
    var path = Path.Combine(puzzlePath, $"puzzle-{puzzleDate}{suffix}.json");
    if (File.Exists(path)) return path;
    // Fall back to standard variant
    if (suffix.Length > 0)
    {
        var fallback = Path.Combine(puzzlePath, $"puzzle-{puzzleDate}.json");
        if (File.Exists(fallback)) return fallback;
    }
    return null;
}

app.MapPost("/api/puzzle/check", (PuzzleCheckRequest body, SubmissionTokenService tokenService) =>
{
    if (string.IsNullOrWhiteSpace(body.Token))
        return Results.Json(new { error = "Missing token" }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(body.PuzzleDate) ||
        !DateOnly.TryParseExact(body.PuzzleDate, "yyyy-MM-dd", out _))
        return Results.BadRequest(new { error = "Invalid puzzle date" });

    var validation = tokenService.ValidateAccess(body.Token);
    if (!validation.IsValid)
        return Results.Json(new { error = validation.Error }, statusCode: 403);

    var filePath = ResolvePuzzleFile(body.PuzzleDate, body.Size);
    if (filePath is null)
        return Results.NotFound(new { error = "Puzzle not found" });

    var answers = SubmissionTokenService.ReadAnswers(filePath);
    if (answers is null)
        return Results.StatusCode(500);

    var results = new Dictionary<string, bool>();
    var allCorrect = true;
    var allFilled = true;

    foreach (var (key, answer) in answers)
    {
        if (body.Cells.TryGetValue(key, out var submitted) && !string.IsNullOrEmpty(submitted))
        {
            var correct = string.Equals(submitted, answer, StringComparison.OrdinalIgnoreCase);
            results[key] = correct;
            if (!correct) allCorrect = false;
        }
        else
        {
            results[key] = false;
            allCorrect = false;
            allFilled = false;
        }
    }

    return Results.Ok(new { solved = allCorrect && allFilled, results });
}).RequireRateLimiting("puzzle-interact");

app.MapPost("/api/puzzle/hint", (PuzzleHintRequest body, SubmissionTokenService tokenService) =>
{
    if (string.IsNullOrWhiteSpace(body.Token))
        return Results.Json(new { error = "Missing token" }, statusCode: 403);

    if (string.IsNullOrWhiteSpace(body.PuzzleDate) ||
        !DateOnly.TryParseExact(body.PuzzleDate, "yyyy-MM-dd", out _))
        return Results.BadRequest(new { error = "Invalid puzzle date" });

    if (body.Cells is not { Length: > 0 } || body.Cells.Length > 50)
        return Results.BadRequest(new { error = "Invalid cells" });

    var validation = tokenService.ValidateAccess(body.Token);
    if (!validation.IsValid)
        return Results.Json(new { error = validation.Error }, statusCode: 403);

    var filePath = ResolvePuzzleFile(body.PuzzleDate, body.Size);
    if (filePath is null)
        return Results.NotFound(new { error = "Puzzle not found" });

    var answers = SubmissionTokenService.ReadAnswers(filePath);
    if (answers is null)
        return Results.StatusCode(500);

    var letters = new Dictionary<string, string>();
    foreach (var cell in body.Cells)
    {
        if (cell.Length != 2) continue;
        var key = $"{cell[0]},{cell[1]}";
        if (answers.TryGetValue(key, out var letter))
            letters[key] = letter;
    }

    return Results.Ok(new { letters });
}).RequireRateLimiting("puzzle-interact");

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

// Make Program accessible to WebApplicationFactory in integration tests

