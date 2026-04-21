namespace SwedishCrossword.Api;

internal static class AnalyticsEndpoints
{
    internal static WebApplication MapAnalyticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/analytics/summary", async (IAnalyticsStore store) =>
        {
            var summary = await store.GetAnalyticsSummaryAsync();
            return Results.Ok(summary);
        }).RequireAuthorization("Admin");

        app.MapGet("/api/analytics/daily", async (int? days, IAnalyticsStore store) =>
        {
            var d = Math.Clamp(days ?? 30, 1, 90);
            var daily = await store.GetDailyAnalyticsAsync(d);
            return Results.Ok(daily);
        }).RequireAuthorization("Admin");

        app.MapGet("/api/analytics/players", async (int? limit, IAnalyticsStore store) =>
        {
            var n = Math.Clamp(limit ?? 10, 1, 50);
            var players = await store.GetTopPlayersAsync(n);
            return Results.Ok(players);
        }).RequireAuthorization("Admin");

        return app;
    }
}
