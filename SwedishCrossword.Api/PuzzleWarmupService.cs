using System.Globalization;
using SwedishCrossword.Services;

namespace SwedishCrossword.Api;

/// <summary>
/// Background service that pre-generates today's puzzle at startup and
/// checks hourly so the first visitor never waits for generation.
/// </summary>
sealed class PuzzleWarmupService : BackgroundService
{
    private static readonly TimeSpan ClueChangeInactivityWindow = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SchedulerPollInterval = TimeSpan.FromSeconds(5);

    private readonly CrosswordGenerator _generator;
    private readonly PrintService _printService;
    private readonly string _puzzlePath;
    private readonly ILogger<PuzzleWarmupService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly PuzzleDateIndex _dateIndex;
    private readonly SemaphoreSlim _regenerationLock = new(1, 1);
    private int _queuedFutureRegeneration;
    private int _pendingChangeCount;
    private long _queuedNotBeforeUnixTimeMs;
    private long _lastQueuedUnixTimeMs;
    private long _lastRunStartedUnixTimeMs;
    private long _lastRunCompletedUnixTimeMs;
    private int _isRegenerating;
    private string? _lastRunError;

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

    /// <summary>
    /// Deletes all future pre-generated puzzle files and regenerates them.
    /// Called on startup and can be triggered manually via the admin panel.
    /// </summary>
    public async Task RegenerateFutureAsync(CancellationToken ct)
    {
        await _regenerationLock.WaitAsync(ct);
        try
        {
            Interlocked.Exchange(ref _isRegenerating, 1);
            Interlocked.Exchange(ref _lastRunStartedUnixTimeMs, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            Volatile.Write(ref _lastRunError, null);

            var today = _timeProvider.GetSwedishDate();
            DeleteFuturePuzzles(today);
            await EnsurePuzzlesForRange(today, DaysAhead, ct);

            Interlocked.Exchange(ref _lastRunCompletedUnixTimeMs, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastRunError, ex.Message);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _isRegenerating, 0);
            _regenerationLock.Release();
        }
    }

    /// <summary>
    /// Queues a future puzzle regeneration to run after clue-change inactivity.
    /// </summary>
    public void QueueFutureRegenerationFromClueChange()
    {
        var now = _timeProvider.GetUtcNow();
        Interlocked.Increment(ref _pendingChangeCount);
        Interlocked.Exchange(ref _lastQueuedUnixTimeMs, now.ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref _queuedNotBeforeUnixTimeMs, now.Add(ClueChangeInactivityWindow).ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref _queuedFutureRegeneration, 1);
    }

    /// <summary>
    /// Queues immediate regeneration (manual admin trigger).
    /// </summary>
    public void QueueFutureRegenerationNow()
    {
        var now = _timeProvider.GetUtcNow();
        Interlocked.Exchange(ref _lastQueuedUnixTimeMs, now.ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref _queuedNotBeforeUnixTimeMs, now.ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref _queuedFutureRegeneration, 1);
    }

    /// <summary>
    /// Returns scheduler state for admin visibility.
    /// </summary>
    public PuzzleRegenerationStatusResponse GetFutureRegenerationStatus()
    {
        var running = Volatile.Read(ref _isRegenerating) == 1;
        var queued = Volatile.Read(ref _queuedFutureRegeneration) == 1;
        var lastError = Volatile.Read(ref _lastRunError);

        var state = running
            ? "running"
            : queued
                ? "queued"
                : string.IsNullOrWhiteSpace(lastError)
                    ? "idle"
                    : "failed";

        return new PuzzleRegenerationStatusResponse(
            State: state,
            PendingChangeCount: Math.Max(0, Volatile.Read(ref _pendingChangeCount)),
            NotBeforeAt: ToNullableUnix(Interlocked.Read(ref _queuedNotBeforeUnixTimeMs)),
            LastQueuedAt: ToNullableUnix(Interlocked.Read(ref _lastQueuedUnixTimeMs)),
            LastStartedAt: ToNullableUnix(Interlocked.Read(ref _lastRunStartedUnixTimeMs)),
            LastCompletedAt: ToNullableUnix(Interlocked.Read(ref _lastRunCompletedUnixTimeMs)),
            LastError: lastError);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // On every startup regenerate future puzzles so they are rebuilt with the
        // latest generator code after each deployment.
        await RegenerateFutureAsync(stoppingToken);

        var nextEnsureRunAt = _timeProvider.GetUtcNow().AddHours(1);

        // Poll frequently enough to react quickly to queued admin updates while
        // still running the normal hourly warmup sweep.
        using var timer = new PeriodicTimer(SchedulerPollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = _timeProvider.GetUtcNow();
            var nowUnix = now.ToUnixTimeMilliseconds();
            var queued = Volatile.Read(ref _queuedFutureRegeneration) == 1;
            var notBeforeUnix = Interlocked.Read(ref _queuedNotBeforeUnixTimeMs);

            if (queued && (notBeforeUnix <= 0 || nowUnix >= notBeforeUnix))
            {
                Interlocked.Exchange(ref _queuedFutureRegeneration, 0);
                Interlocked.Exchange(ref _pendingChangeCount, 0);

                _logger.LogInformation("Running queued future puzzle regeneration");
                try
                {
                    await RegenerateFutureAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queued future puzzle regeneration failed");
                }

                nextEnsureRunAt = _timeProvider.GetUtcNow().AddHours(1);
                continue;
            }

            if (now >= nextEnsureRunAt)
            {
                await EnsurePuzzlesForRange(_timeProvider.GetSwedishDate(), DaysAhead, stoppingToken);
                nextEnsureRunAt = _timeProvider.GetUtcNow().AddHours(1);
            }
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

    private void DeleteFuturePuzzles(DateOnly today)
    {
        for (var offset = 1; offset <= DaysAhead; offset++)
        {
            var date = today.AddDays(offset);
            foreach (var (sizeKey, _) in PuzzleSizes)
            {
                var filePath = Path.Combine(_puzzlePath, $"puzzle-{date:yyyy-MM-dd}-{sizeKey}.json");
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        _logger.LogInformation("Deleted future puzzle {Size} for {Date} — will regenerate with latest code", sizeKey, date);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete future puzzle file {Path}", filePath);
                }
            }
        }
    }

    private static long? ToNullableUnix(long value) => value > 0 ? value : null;
}
