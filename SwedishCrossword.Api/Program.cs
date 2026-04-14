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

// Default size when none specified
const string DefaultPuzzleSize = "17x17";

app.MapGet("/api/puzzle/today", async (string? size, SubmissionTokenService tokenService) =>
{
    var sizeKey = NormalisePuzzleSize(size);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var todayFile = ResolvePuzzleFileForSize(puzzlePath, today, sizeKey);

    if (todayFile is not null)
    {
        var json = await File.ReadAllTextAsync(todayFile);
        return Results.Content(tokenService.InjectToken(json, today), "application/json");
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

    var sizeKey = NormalisePuzzleSize(size);
    var puzzleFile = ResolvePuzzleFileForSize(puzzlePath, parsedDate, sizeKey);

    if (puzzleFile is not null)
    {
        var json = await File.ReadAllTextAsync(puzzleFile);
        return Results.Content(tokenService.InjectToken(json, parsedDate), "application/json");
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

// Helper: normalise a size query parameter to a known size key (e.g. "10x10").
// Falls back to DefaultPuzzleSize and also maps the legacy "small" value.
string NormalisePuzzleSize(string? size)
{
    if (string.IsNullOrWhiteSpace(size)) return DefaultPuzzleSize;
    // Legacy compat: "small" → "10x10"
    if (string.Equals(size, "small", StringComparison.OrdinalIgnoreCase)) return "10x10";
    if (PuzzleWarmupService.ValidSizeKeys.Contains(size)) return size;
    return DefaultPuzzleSize;
}

// Helper: resolve the puzzle file path for a given date and size key.
// Tries the new naming convention first, then falls back to legacy names,
// and finally to the default size.
string? ResolvePuzzleFileForSize(string basePath, DateOnly date, string sizeKey)
{
    // New naming: puzzle-{date}-{size}.json
    var path = Path.Combine(basePath, $"puzzle-{date:yyyy-MM-dd}-{sizeKey}.json");
    if (File.Exists(path)) return path;

    // Legacy naming: puzzle-{date}.json (17x17) and puzzle-{date}-small.json (10x10)
    if (string.Equals(sizeKey, "17x17", StringComparison.Ordinal))
    {
        var legacy = Path.Combine(basePath, $"puzzle-{date:yyyy-MM-dd}.json");
        if (File.Exists(legacy)) return legacy;
    }
    else if (string.Equals(sizeKey, "10x10", StringComparison.Ordinal))
    {
        var legacy = Path.Combine(basePath, $"puzzle-{date:yyyy-MM-dd}-small.json");
        if (File.Exists(legacy)) return legacy;
    }

    // Fall back to default size
    if (!string.Equals(sizeKey, DefaultPuzzleSize, StringComparison.Ordinal))
    {
        var fallback = Path.Combine(basePath, $"puzzle-{date:yyyy-MM-dd}-{DefaultPuzzleSize}.json");
        if (File.Exists(fallback)) return fallback;
        var legacyFallback = Path.Combine(basePath, $"puzzle-{date:yyyy-MM-dd}.json");
        if (File.Exists(legacyFallback)) return legacyFallback;
    }

    return null;
}

// Legacy helper used by check/hint endpoints (delegates to size-aware resolver)
string? ResolvePuzzleFile(string puzzleDate, string? size)
{
    if (!DateOnly.TryParseExact(puzzleDate, "yyyy-MM-dd", out var parsed))
        return null;
    return ResolvePuzzleFileForSize(puzzlePath, parsed, NormalisePuzzleSize(size));
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

    if (body.Cells is not { Length: > 0 } || body.Cells.Length > 300)
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
    var files = Directory.GetFiles(puzzlePath, "puzzle-*.json");

    // Collect unique dates and which sizes are available for each
    var dateSizes = new Dictionary<string, HashSet<string>>();
    foreach (var f in files)
    {
        var name = Path.GetFileNameWithoutExtension(f).Replace("puzzle-", "");
        // New format: yyyy-MM-dd-WxH  Legacy: yyyy-MM-dd or yyyy-MM-dd-small
        string datePart;
        string sizeKey;
        if (name.Length > 10 && name[10] == '-')
        {
            datePart = name[..10];
            var sizePart = name[11..];
            sizeKey = string.Equals(sizePart, "small", StringComparison.OrdinalIgnoreCase) ? "10x10" : sizePart;
        }
        else
        {
            datePart = name;
            sizeKey = "17x17";
        }

        if (!DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out var parsed) || parsed > today)
            continue;

        if (!dateSizes.TryGetValue(datePart, out var sizes))
        {
            sizes = [];
            dateSizes[datePart] = sizes;
        }
        sizes.Add(sizeKey);
    }

    var result = dateSizes
        .OrderByDescending(kv => kv.Key)
        .Select(kv => new { date = kv.Key, sizes = kv.Value.OrderBy(s => s).ToArray() })
        .ToArray();

    return Results.Ok(result);
}).CacheOutput("puzzle-dates");

// ---------------------------------------------------------------------------
// Stats
// ---------------------------------------------------------------------------

string[] availableDifficulties = ["easy", "medium", "hard", "small", "mobile"];
string[] availableSizes = PuzzleWarmupService.PuzzleSizes.Select(s => s.Key).ToArray();

app.MapGet("/api/stats", (SwedishDictionary dictionary) =>
{
    return Results.Ok(new
    {
        wordCount = dictionary.WordCount,
        availableDifficulties,
        availableSizes
    });
});

app.MapHealthChecks("/api/health");

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests

