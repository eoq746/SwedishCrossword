using SwedishCrossword.Services;

namespace SwedishCrossword.Api;

internal static class StatsEndpoints
{
    private static readonly string[] AvailableDifficulties = ["easy", "medium", "hard", "small", "mobile"];
    private static readonly string[] AvailableSizes = PuzzleWarmupService.PuzzleSizes.Select(s => s.Key).ToArray();

    internal static WebApplication MapStatsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/stats", (SwedishDictionary dictionary) =>
        {
            return Results.Ok(new
            {
                wordCount = dictionary.WordCount,
                availableDifficulties = AvailableDifficulties,
                availableSizes = AvailableSizes
            });
        });

        return app;
    }
}
