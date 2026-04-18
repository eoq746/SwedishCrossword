using System.Security.Claims;

namespace SwedishCrossword.Api;

internal static class LeaderboardEndpoints
{
    internal static WebApplication MapLeaderboardEndpoints(this WebApplication app)
    {
        var logger = app.Logger;

        app.MapGet("/api/leaderboard", async (LeaderboardStore store) =>
        {
            var data = await store.GetCurrentAsync();
            return Results.Content(data, "application/json");
        });

        app.MapPost("/api/scores", async (ScoreSubmissionRequest body, SubmissionTokenService tokenService, LeaderboardStore store, TimeProvider timeProvider, ClaimsPrincipal user) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            var name = LeaderboardStore.SanitiseName(body.Name);

            // Authenticated users must use their alias
            if (userId is not null)
            {
                var alias = await store.GetAliasAsync(userId);
                if (!string.IsNullOrWhiteSpace(alias))
                    name = alias;
            }

            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new ErrorResponse("Invalid name"));

            if (body.Time < 0 || body.Time > 86400)
                return Results.BadRequest(new ErrorResponse("Invalid time"));

            if (string.IsNullOrWhiteSpace(body.Token))
                return Results.Json(new ErrorResponse("Missing submission token"), statusCode: 403);

            if (string.IsNullOrWhiteSpace(body.PuzzleHash))
                return Results.BadRequest(new ErrorResponse("Missing puzzle hash"));

            if (string.IsNullOrWhiteSpace(body.Date) || !LeaderboardStore.DatePattern.IsMatch(body.Date))
                return Results.BadRequest(new ErrorResponse("Invalid date format"));

            var validation = tokenService.Validate(body.Token, body.PuzzleHash, body.Time);
            if (!validation.IsValid)
                return Results.Json(new ErrorResponse(validation.Error), statusCode: 403);

            var leaderboardKey = $"{body.Date}-{body.PuzzleHash}";
            var timestamp = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var entry = new ScoreRecord(name, body.Time, timestamp, body.PuzzleHash, body.HintsUsed, body.WordHintsUsed, userId);
            var leaderboard = await store.AppendScoreAsync(leaderboardKey, entry);

            // Also archive to historical leaderboard (best-effort; don't fail the request)
            try
            {
                await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Time, timestamp, body.PuzzleHash, body.PuzzleSize, body.HintsUsed, body.WordHintsUsed, userId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to archive history for {Date}/{Hash}", body.Date, body.PuzzleHash);
            }

            return Results.Ok(new { success = true, leaderboard });
        }).RequireRateLimiting("leaderboard-write");

        app.MapPost("/api/leaderboard/history", async (LeaderboardHistoryRequest body, SubmissionTokenService tokenService, LeaderboardStore store, TimeProvider timeProvider, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(body.Token))
                return Results.Json(new ErrorResponse("Missing submission token"), statusCode: 403);

            if (string.IsNullOrWhiteSpace(body.Date) || !LeaderboardStore.DatePattern.IsMatch(body.Date))
                return Results.BadRequest(new ErrorResponse("Invalid date format"));

            var today = timeProvider.GetSwedishDate();
            if (!DateOnly.TryParseExact(body.Date, "yyyy-MM-dd", out var historyDate)
                || historyDate < today.AddDays(-90)
                || historyDate > today.AddDays(1))
                return Results.BadRequest(new ErrorResponse("Date out of range"));

            if (body.Entry is null || body.Entry.Time < 0 || body.Entry.Time > 86400)
                return Results.BadRequest(new ErrorResponse("Invalid entry"));

            if (string.IsNullOrWhiteSpace(body.Entry.PuzzleHash))
                return Results.BadRequest(new ErrorResponse("Missing puzzle hash"));

            var validation = tokenService.Validate(body.Token, body.Entry.PuzzleHash, body.Entry.Time);
            if (!validation.IsValid)
                return Results.Json(new ErrorResponse(validation.Error), statusCode: 403);

            var name = LeaderboardStore.SanitiseName(body.Entry.Name);
            // Authenticated users must use their alias
            var historyUserId = AuthEndpoints.GetUserId(user);
            if (historyUserId is not null)
            {
                var alias = await store.GetAliasAsync(historyUserId);
                if (!string.IsNullOrWhiteSpace(alias))
                    name = alias;
            }

            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new ErrorResponse("Invalid name"));

            await store.AppendHistoryAsync(body.Date, new HistoryRecord(name, body.Entry.Time, body.Entry.Timestamp, body.Entry.PuzzleHash, body.Entry.PuzzleSize, body.Entry.HintsUsed, body.Entry.WordHintsUsed, historyUserId));
            return Results.Ok(new { ok = true });
        }).RequireRateLimiting("leaderboard-write");

        app.MapGet("/api/leaderboard/history", async (int? days, LeaderboardStore store) =>
        {
            var d = Math.Clamp(days ?? 30, 1, 90);
            var history = await store.GetHistoryAsync(d);
            return Results.Ok(history);
        });

        return app;
    }
}
