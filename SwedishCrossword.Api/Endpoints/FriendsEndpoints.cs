using System.Security.Claims;

namespace SwedishCrossword.Api;

internal static class FriendsEndpoints
{
    internal static WebApplication MapFriendsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/friends").RequireAuthorization().RequireRateLimiting("friends");

        // List accepted friends
        group.MapGet("/", async (ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var friends = await store.GetFriendsAsync(userId);
            return Results.Ok(friends);
        });

        // List pending friend requests (incoming + outgoing)
        group.MapGet("/requests", async (ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var requests = await store.GetPendingRequestsAsync(userId);
            return Results.Ok(requests);
        });

        // Send friend request by alias
        group.MapPost("/request", async (FriendRequestDto body, ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var alias = LeaderboardStore.SanitiseName(body.Alias);
            if (string.IsNullOrWhiteSpace(alias))
                return Results.BadRequest(new ErrorResponse("Ogiltigt alias"));

            // Sender must have an alias set
            var senderAlias = await store.GetAliasAsync(userId);
            if (string.IsNullOrWhiteSpace(senderAlias))
                return Results.BadRequest(new ErrorResponse("Du måste sätta ett alias innan du kan lägga till vänner"));

            var targetUserId = await store.GetUserIdByAliasAsync(alias);
            if (targetUserId is null)
                return Results.NotFound(new ErrorResponse("Ingen användare med det aliaset hittades"));

            var (success, error) = await store.SendFriendRequestAsync(userId, targetUserId);
            if (!success)
                return Results.Conflict(new ErrorResponse(error));

            return Results.Ok(new { ok = true });
        });

        // Accept a friend request
        group.MapPost("/accept/{requestId}", async (string requestId, ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var accepted = await store.AcceptFriendRequestAsync(requestId, userId);
            return accepted
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Förfrågan hittades inte"));
        });

        // Decline a friend request
        group.MapPost("/decline/{requestId}", async (string requestId, ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var declined = await store.DeclineFriendRequestAsync(requestId, userId);
            return declined
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Förfrågan hittades inte"));
        });

        // Remove a friend
        group.MapDelete("/{friendshipId}", async (string friendshipId, ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var removed = await store.RemoveFriendAsync(userId, friendshipId);
            return removed
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Vänskapen hittades inte"));
        });

        // Friends leaderboard for a specific date, optionally filtered by puzzle hash
        group.MapGet("/leaderboard", async (string? date, string? puzzleHash, ClaimsPrincipal user, LeaderboardStore store, TimeProvider timeProvider) =>
        {
            var userId = AuthEndpoints.GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var d = date ?? timeProvider.GetSwedishDate().ToString("yyyy-MM-dd");
            if (!LeaderboardStore.DatePattern.IsMatch(d))
                return Results.BadRequest(new ErrorResponse("Ogiltigt datumformat"));

            var leaderboard = await store.GetFriendsLeaderboardAsync(userId, d, puzzleHash);
            return Results.Ok(leaderboard);
        });

        return app;
    }
}
