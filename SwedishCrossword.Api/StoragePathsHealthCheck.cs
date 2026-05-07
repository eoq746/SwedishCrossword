using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SwedishCrossword.Api;

internal sealed class StoragePathsHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var puzzlePath = configuration["Storage:PuzzlePath"];
        if (string.IsNullOrWhiteSpace(puzzlePath))
            puzzlePath = Path.Combine(AppContext.BaseDirectory, "puzzles");

        var leaderboardPath = configuration["Storage:LeaderboardPath"];
        if (string.IsNullOrWhiteSpace(leaderboardPath))
            leaderboardPath = Path.Combine(AppContext.BaseDirectory, "leaderboard");

        var puzzleCheck = await CanWriteAsync(puzzlePath, cancellationToken);
        if (puzzleCheck.Status != HealthStatus.Healthy)
            return puzzleCheck;

        var leaderboardCheck = await CanWriteAsync(leaderboardPath, cancellationToken);
        if (leaderboardCheck.Status != HealthStatus.Healthy)
            return leaderboardCheck;

        return HealthCheckResult.Healthy();
    }

    private static async Task<HealthCheckResult> CanWriteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, $".health-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);
            return HealthCheckResult.Healthy();
        }
        catch (UnauthorizedAccessException ex)
        {
            return HealthCheckResult.Unhealthy($"Storage path is not writable: {path}", ex);
        }
        catch (IOException ex)
        {
            return HealthCheckResult.Unhealthy($"Storage path check failed: {path}", ex);
        }
        catch (ArgumentException ex)
        {
            return HealthCheckResult.Unhealthy($"Storage path is invalid: {path}", ex);
        }
        catch (NotSupportedException ex)
        {
            return HealthCheckResult.Unhealthy($"Storage path is not supported: {path}", ex);
        }
    }
}
