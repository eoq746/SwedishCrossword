using SwedishCrossword.Services;

namespace SwedishCrossword.Api;

/// <summary>
/// Background service that pre-generates today's puzzle at startup and
/// checks hourly so the first visitor never waits for generation.
/// </summary>
sealed class PuzzleWarmupService : BackgroundService
{
    private readonly CrosswordGenerator _generator;
    private readonly PrintService _printService;
    private readonly string _puzzlePath;
    private readonly ILogger<PuzzleWarmupService> _logger;

    public PuzzleWarmupService(
        CrosswordGenerator generator,
        PrintService printService,
        IConfiguration config,
        ILogger<PuzzleWarmupService> logger)
    {
        _generator = generator;
        _printService = printService;
        _logger = logger;

        var path = config["Storage:PuzzlePath"];
        _puzzlePath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "puzzles")
            : path;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Generate today's puzzle and the next 7 days immediately on startup
        await EnsurePuzzlesForRange(DateOnly.FromDateTime(DateTime.UtcNow), DaysAhead, stoppingToken);

        // Then check every hour (catches the date rollover at midnight UTC)
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EnsurePuzzlesForRange(DateOnly.FromDateTime(DateTime.UtcNow), DaysAhead, stoppingToken);
        }
    }

    /// <summary>Number of days to pre-generate ahead of today.</summary>
    private const int DaysAhead = 7;

    private async Task EnsurePuzzlesForRange(DateOnly startDate, int daysAhead, CancellationToken ct)
    {
        for (var offset = 0; offset <= daysAhead; offset++)
        {
            if (ct.IsCancellationRequested) return;
            await EnsurePuzzleForDate(startDate.AddDays(offset), ct);
        }
    }

    private async Task EnsurePuzzleForDate(DateOnly date, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_puzzlePath);
            var filePath = Path.Combine(_puzzlePath, $"puzzle-{date:yyyy-MM-dd}.json");

            if (File.Exists(filePath))
            {
                _logger.LogDebug("Puzzle for {Date} already exists", date);
                return;
            }

            _logger.LogInformation("Pre-generating puzzle for {Date}...", date);

            var puzzle = await _generator.GenerateAsync(CrosswordGenerationOptions.Hard, ct);
            var json = _printService.GenerateJsonForWeb(puzzle);

            await File.WriteAllTextAsync(filePath, json, ct);

            _logger.LogInformation("Puzzle for {Date} generated successfully", date);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown requested — expected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pre-generate puzzle for {Date}", date);
        }
    }
}
