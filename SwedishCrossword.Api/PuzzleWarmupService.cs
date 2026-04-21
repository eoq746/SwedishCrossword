using System.Globalization;
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
    private readonly TimeProvider _timeProvider;
    private readonly PuzzleDateIndex _dateIndex;

    /// <summary>
    /// Puzzle sizes to generate for each day. Add new entries here to extend
    /// the available sizes without changing any other code.
    /// </summary>
    internal static readonly (string Key, CrosswordGenerationOptions Options)[] PuzzleSizes =
    [
        ("10x10", CrosswordGenerationOptions.Mobile),
        ("15x15", CrosswordGenerationOptions.Medium),
        ("17x17", CrosswordGenerationOptions.Hard),
    ];

    /// <summary>Set of valid size keys for fast lookup.</summary>
    internal static readonly HashSet<string> ValidSizeKeys =
        new(PuzzleSizes.Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

    public PuzzleWarmupService(
        CrosswordGenerator generator,
        PrintService printService,
        IConfiguration config,
        ILogger<PuzzleWarmupService> logger,
        TimeProvider timeProvider,
        PuzzleDateIndex dateIndex)
    {
        _generator = generator;
        _printService = printService;
        _logger = logger;
        _timeProvider = timeProvider;
        _dateIndex = dateIndex;

        var path = config["Storage:PuzzlePath"];
        _puzzlePath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "puzzles")
            : path;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Generate today's puzzle and the next 7 days immediately on startup
        await EnsurePuzzlesForRange(_timeProvider.GetSwedishDate(), DaysAhead, stoppingToken);

        // Then check every hour (catches the date rollover at midnight Swedish time)
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EnsurePuzzlesForRange(_timeProvider.GetSwedishDate(), DaysAhead, stoppingToken);
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

            foreach (var (sizeKey, options) in PuzzleSizes)
            {
                if (ct.IsCancellationRequested) return;

                var filePath = Path.Combine(_puzzlePath, $"puzzle-{date:yyyy-MM-dd}-{sizeKey}.json");
                if (!File.Exists(filePath))
                {
                    _logger.LogInformation("Pre-generating {Size} puzzle for {Date}...", sizeKey, date);
                    var puzzle = await _generator.GenerateAsync(options, ct);
                    var json = _printService.GenerateJsonForWeb(puzzle);
                    await File.WriteAllTextAsync(filePath, json, ct);
                    _dateIndex.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), sizeKey);
                    _logger.LogInformation("{Size} puzzle for {Date} generated successfully", sizeKey, date);
                }
                else
                {
                    _logger.LogDebug("{Size} puzzle for {Date} already exists", sizeKey, date);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown requested — expected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pre-generate puzzles for {Date}", date);
        }
    }
}
