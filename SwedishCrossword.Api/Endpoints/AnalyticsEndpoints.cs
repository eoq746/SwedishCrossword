using System.Security.Claims;

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

        /// <summary>
        /// Deletes all pre-generated future puzzles and immediately regenerates them
        /// using the latest generator code. Today's puzzle is preserved.
        /// </summary>
        app.MapPost("/api/admin/puzzle/regenerate-future", async (PuzzleWarmupService warmup, CancellationToken ct) =>
        {
            await warmup.RegenerateFutureAsync(ct);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("Admin");

        // ── Admin user management ──

        /// <summary>Look up a registered user by their alias.</summary>
        app.MapGet("/api/admin/users/search", async (string q, IUserProfileStore profileStore) =>
        {
            var trimmed = q.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return Results.BadRequest(new ErrorResponse("Query is required"));
            var userId = await profileStore.GetUserIdByAliasAsync(trimmed);
            if (userId is null)
                return Results.NotFound(new ErrorResponse("No user found with that alias"));
            return Results.Ok(new { userId, alias = trimmed });
        }).RequireAuthorization("Admin");

        /// <summary>List all DB-granted admins with their aliases.</summary>
        app.MapGet("/api/admin/grants", async (IAdminStore adminStore) =>
        {
            var admins = await adminStore.ListGrantedAdminsAsync();
            return Results.Ok(admins);
        }).RequireAuthorization("Admin");

        /// <summary>Grant admin rights to a user.</summary>
        app.MapPost("/api/admin/grants", async (GrantAdminRequest body, ClaimsPrincipal user, IAdminStore adminStore) =>
        {
            if (string.IsNullOrWhiteSpace(body.UserId))
                return Results.BadRequest(new ErrorResponse("Missing userId"));
            var grantedBy = AuthEndpoints.GetUserId(user)!;
            await adminStore.GrantAdminAsync(body.UserId, grantedBy);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("Admin");

        /// <summary>Revoke admin rights from a DB-granted admin.</summary>
        app.MapDelete("/api/admin/grants/{userId}", async (string userId, IAdminStore adminStore) =>
        {
            await adminStore.RevokeAdminAsync(userId);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization("Admin");

        return app;
    }
}
