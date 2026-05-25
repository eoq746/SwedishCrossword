using System.Globalization;
using System.Security.Claims;

namespace SwedishCrossword.Api.Endpoints;

internal static class FriendsEndpoints
{
    internal static WebApplication MapFriendsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/friends").RequireAuthorization().RequireRateLimiting("friends");

        // List accepted friends
        group.MapGet("/", async (ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var friends = await store.GetFriendsAsync(userId);
            return Results.Ok(friends);
        });

        // List pending friend requests (incoming + outgoing)
        group.MapGet("/requests", async (ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var requests = await store.GetPendingRequestsAsync(userId);
            return Results.Ok(requests);
        });

        // List friend challenges
        group.MapGet("/challenges", async (ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var challenges = await store.GetChallengesAsync(userId);
            return Results.Ok(challenges);
        });

        static bool TryValidateChallengeContext(string date, string puzzleSize, TimeProvider timeProvider, out IResult? error)
        {
            error = null;
            if (!LeaderboardStore.DatePattern.IsMatch(date) ||
                !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var challengeDate))
            {
                error = Results.BadRequest(new ErrorResponse("Ogiltigt datumformat"));
                return false;
            }

            if (string.IsNullOrWhiteSpace(puzzleSize))
            {
                error = Results.BadRequest(new ErrorResponse("Storlek saknas"));
                return false;
            }

            var today = timeProvider.GetSwedishDate();
            var minDate = today.AddDays(-365);
            var maxDate = today.AddDays(30);
            if (challengeDate < minDate || challengeDate > maxDate)
            {
                error = Results.BadRequest(new ErrorResponse("Datumet för utmaningen ligger utanför tillåtet intervall"));
                return false;
            }

            return true;
        }

        // Create challenge for an accepted friendship
        group.MapPost("/challenges", async (FriendChallengeCreateRequest body, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore, TimeProvider timeProvider) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            if (string.IsNullOrWhiteSpace(body.FriendId))
                return Results.BadRequest(new ErrorResponse("Vänskap saknas"));

            if (!TryValidateChallengeContext(body.Date, body.PuzzleSize, timeProvider, out var errorResult))
                return errorResult!;

            var (success, error) = await store.CreateChallengeAsync(userId, body.FriendId, body.Date, body.PuzzleSize);
            if (!success)
                return Results.Conflict(new ErrorResponse(error));

            return Results.Ok(new { ok = true });
        });

        // Create challenges for selected friends or all friends
        group.MapPost("/challenges/bulk", async (FriendChallengesCreateRequest body, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore, TimeProvider timeProvider) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            if (!TryValidateChallengeContext(body.Date, body.PuzzleSize, timeProvider, out var errorResult))
                return errorResult!;

            var friends = await store.GetFriendsAsync(userId);
            var targetFriendIds = body.AllFriends
                ? [.. friends.Select(f => f.FriendId).Distinct(StringComparer.Ordinal)]
                : (body.FriendIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();

            if (targetFriendIds.Length == 0)
                return Results.BadRequest(new ErrorResponse("Inga vänner valda"));

            var result = await store.CreateChallengesAsync(userId, targetFriendIds, body.Date, body.PuzzleSize);
            return Results.Ok(result);
        });

        // Respond to challenge (incoming only)
        group.MapPost("/challenges/{challengeId}/respond", async (string challengeId, FriendChallengeRespondRequest body, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var changed = await store.RespondToChallengeAsync(challengeId, userId, body.Accepted);
            return changed
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Utmaningen hittades inte"));
        });

        // Send friend request by alias
        group.MapPost("/request", async (FriendRequestDto body, ClaimsPrincipal user, IFriendStore friendStore, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var alias = LeaderboardStore.SanitiseName(body.Alias);
            if (string.IsNullOrWhiteSpace(alias))
                return Results.BadRequest(new ErrorResponse("Ogiltigt alias"));

            // Sender must have an alias set
            var senderAlias = await profileStore.GetAliasAsync(userId);
            if (string.IsNullOrWhiteSpace(senderAlias))
                return Results.BadRequest(new ErrorResponse("Du måste sätta ett alias innan du kan lägga till vänner"));

            var targetUserId = await profileStore.GetUserIdByAliasAsync(alias);
            if (targetUserId is null)
                return Results.NotFound(new ErrorResponse("Ingen användare med det aliaset hittades"));

            var (success, error) = await friendStore.SendFriendRequestAsync(userId, targetUserId);
            if (!success)
                return Results.Conflict(new ErrorResponse(error));

            return Results.Ok(new { ok = true });
        });

        // Accept a friend request
        group.MapPost("/accept/{requestId}", async (string requestId, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var accepted = await store.AcceptFriendRequestAsync(requestId, userId);
            return accepted
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Förfrågan hittades inte"));
        });

        // Decline a friend request
        group.MapPost("/decline/{requestId}", async (string requestId, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var declined = await store.DeclineFriendRequestAsync(requestId, userId);
            return declined
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Förfrågan hittades inte"));
        });

        // Remove a friend
        group.MapDelete("/{friendshipId}", async (string friendshipId, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var removed = await store.RemoveFriendAsync(userId, friendshipId);
            return removed
                ? Results.Ok(new { ok = true })
                : Results.NotFound(new ErrorResponse("Vänskapen hittades inte"));
        });

        // Friends leaderboard for a specific date, optionally filtered by puzzle hash
        group.MapGet("/leaderboard", async (string? date, string? puzzleHash, ClaimsPrincipal user, IFriendStore store, IUserProfileStore profileStore, TimeProvider timeProvider) =>
        {
            var userId = await AuthEndpoints.ResolveUserIdAsync(user, profileStore);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var d = date ?? timeProvider.GetSwedishDate().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!LeaderboardStore.DatePattern.IsMatch(d))
                return Results.BadRequest(new ErrorResponse("Ogiltigt datumformat"));

            var leaderboard = await store.GetFriendsLeaderboardAsync(userId, d, puzzleHash);
            return Results.Ok(leaderboard);
        });

        return app;
    }
}
