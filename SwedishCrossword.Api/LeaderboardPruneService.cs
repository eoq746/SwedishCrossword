namespace SwedishCrossword.Api;

/// <summary>
/// Background service that periodically prunes old leaderboard scores and history,
/// removing this work from the hot path of score submissions.
/// </summary>
sealed class LeaderboardPruneService : BackgroundService
{
    private readonly LeaderboardStore _store;
    private readonly ILogger<LeaderboardPruneService> _logger;

    public LeaderboardPruneService(LeaderboardStore store, ILogger<LeaderboardPruneService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit after startup before first prune
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await _store.PruneOldEntriesAsync();
                _logger.LogDebug("Leaderboard prune completed");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Leaderboard prune failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
