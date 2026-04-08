using SwedishCrossword.Services;

var builder = WebApplication.CreateBuilder(args);

// Register domain services
builder.Services.AddSingleton<SwedishDictionary>();
builder.Services.AddSingleton<GridValidator>();
builder.Services.AddSingleton<ClueGenerator>();
builder.Services.AddSingleton<CrosswordGenerator>();
builder.Services.AddSingleton<PrintService>();

// CORS — allow the frontend origin (tighten in production)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();

// ---------------------------------------------------------------------------
// Puzzle endpoints
// ---------------------------------------------------------------------------

/// <summary>
/// GET /api/puzzle/today — returns today's puzzle JSON.
/// If a pre-generated file exists for today it is served directly;
/// otherwise a new puzzle is generated on the fly.
/// </summary>
app.MapGet("/api/puzzle/today", async (PrintService printService, CrosswordGenerator generator) =>
{
    var todayFile = GetPuzzlePath(DateOnly.FromDateTime(DateTime.UtcNow));

    if (File.Exists(todayFile))
    {
        var json = await File.ReadAllTextAsync(todayFile);
        return Results.Content(json, "application/json");
    }

    // Generate on demand
    var puzzle = await generator.GenerateAsync(CrosswordGenerationOptions.Hard);
    var content = printService.GenerateJsonForWeb(puzzle);

    // Persist so subsequent requests are fast
    Directory.CreateDirectory(Path.GetDirectoryName(todayFile)!);
    await File.WriteAllTextAsync(todayFile, content);

    return Results.Content(content, "application/json");
});

/// <summary>
/// GET /api/puzzle/{date} — returns the puzzle for a specific date (yyyy-MM-dd).
/// </summary>
app.MapGet("/api/puzzle/{date}", async (string date, PrintService printService, CrosswordGenerator generator) =>
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
    {
        return Results.BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });
    }

    var puzzleFile = GetPuzzlePath(parsedDate);

    if (File.Exists(puzzleFile))
    {
        var json = await File.ReadAllTextAsync(puzzleFile);
        return Results.Content(json, "application/json");
    }

    // Only allow generating today or future puzzles on demand
    if (parsedDate < DateOnly.FromDateTime(DateTime.UtcNow))
    {
        return Results.NotFound(new { error = "No puzzle available for the requested date." });
    }

    var puzzle = await generator.GenerateAsync(CrosswordGenerationOptions.Hard);
    var content = printService.GenerateJsonForWeb(puzzle);

    Directory.CreateDirectory(Path.GetDirectoryName(puzzleFile)!);
    await File.WriteAllTextAsync(puzzleFile, content);

    return Results.Content(content, "application/json");
});

/// <summary>
/// POST /api/puzzle/generate — generates a fresh puzzle with optional difficulty.
/// Body (optional): { "difficulty": "easy" | "medium" | "hard" | "small" }
/// </summary>
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
});

// ---------------------------------------------------------------------------
// Dictionary / stats endpoints
// ---------------------------------------------------------------------------

app.MapGet("/api/stats", (SwedishDictionary dictionary) =>
{
    return Results.Ok(new
    {
        wordCount = dictionary.WordCount,
        availableDifficulties = new[] { "easy", "medium", "hard", "small" }
    });
});

app.Run();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string GetPuzzlePath(DateOnly date)
{
    var dataDir = Path.Combine(AppContext.BaseDirectory, "puzzles");
    return Path.Combine(dataDir, $"puzzle-{date:yyyy-MM-dd}.json");
}

// ---------------------------------------------------------------------------
// Request / response models
// ---------------------------------------------------------------------------

record GenerateRequest(string? Difficulty);
