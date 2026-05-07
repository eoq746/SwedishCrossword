namespace SwedishCrossword.Api;

/// <summary>
/// Background service that periodically prunes old leaderboard scores and history,
/// removing this work from the hot path of score submissions.
/// </summary>
sealed class LeaderboardPruneService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaderboardPruneService> _logger;

    public LeaderboardPruneService(IServiceScopeFactory scopeFactory, ILogger<LeaderboardPruneService> logger)
    {
        _scopeFactory = scopeFactory;
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
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IScoreStore>();
                await store.PruneOldEntriesAsync();
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
