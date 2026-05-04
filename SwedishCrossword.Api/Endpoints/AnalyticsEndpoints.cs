using System.Security.Claims;
using SwedishCrossword.Api.Endpoints;
using SwedishCrossword.Services;

namespace SwedishCrossword.Api.Endpoints;

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

        app.MapPost("/api/clues/flags", async (ClueFlagCreateRequest body, ClaimsPrincipal user, IClueFlagStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.Word))
                return Results.BadRequest(new ErrorResponse("Word is required"));
            if (string.IsNullOrWhiteSpace(body.CurrentClue))
                return Results.BadRequest(new ErrorResponse("Current clue is required"));

            var request = new ClueFlagCreateRequest(
                Word: body.Word.Trim().ToUpperInvariant(),
                CurrentClue: body.CurrentClue.Trim(),
                SuggestedClue: string.IsNullOrWhiteSpace(body.SuggestedClue) ? null : body.SuggestedClue.Trim(),
                Reason: string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
                PuzzleDate: body.PuzzleDate,
                PuzzleSize: body.PuzzleSize,
                PuzzleHash: body.PuzzleHash);

            var userId = AuthEndpoints.GetUserId(user);
            var id = await store.CreateClueFlagAsync(request, userId);
            return Results.Ok(new { id });
        });

        app.MapGet("/api/admin/clues/flags", async (int? limit, IClueFlagStore store) =>
        {
            var items = await store.ListPendingClueFlagsAsync(Math.Clamp(limit ?? 100, 1, 200));
            return Results.Ok(items);
        }).RequireAuthorization("Admin");

        app.MapPost("/api/admin/clues/custom", async (
            CreateCustomClueRequest body,
            WordListAdminService wordListService,
            SwedishDictionary dictionary,
            CrosswordGenerator generator,
            PuzzleWarmupService warmup,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Word))
                return Results.BadRequest(new ErrorResponse("Word is required"));
            if (string.IsNullOrWhiteSpace(body.Clue))
                return Results.BadRequest(new ErrorResponse("Clue is required"));

            var created = wordListService.AddCustomWordEntry(
                body.Word,
                body.Clue,
                body.Category,
                body.Difficulty,
                expectedVersion: null);

            if (created.Result == WordListUpdateResult.VersionConflict)
                return Results.Conflict(new ErrorResponse("Word+clue already exists in custom word list"));

            dictionary.Reload();
            generator.RebuildWordAnalysisCache();
            await warmup.RegenerateFutureAsync(ct);

            return Results.Ok(new { ok = true, source = "custom", wordListVersion = created.CurrentVersion });
        }).RequireAuthorization("Admin");

        app.MapPost("/api/admin/wordlists/sync-dev-to-prod", async (
            BlobWordListSyncRequest body,
            BlobWordListSyncService syncService,
            CancellationToken ct) =>
        {
            if (!syncService.IsEnabled)
                return Results.BadRequest(new ErrorResponse("Blob word list sync is not configured"));

            var result = await syncService.SyncDevToProdAsync(body.DryRun, ct);
            return Results.Ok(result);
        }).RequireAuthorization("Admin");

        app.MapPost("/api/admin/clues/flags/{id}/resolve", async (
            string id,
            ClueFlagResolveRequest body,
            ClaimsPrincipal user,
            IClueFlagStore clueFlagStore,
            WordListAdminService wordListService,
            SwedishDictionary dictionary,
            CrosswordGenerator generator,
            PuzzleWarmupService warmup,
            CancellationToken ct) =>
        {
            var adminUserId = AuthEndpoints.GetUserId(user);
            if (adminUserId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var flag = await clueFlagStore.GetClueFlagAsync(id);
            if (flag is null)
                return Results.NotFound(new ErrorResponse("Clue flag not found"));

            if (!string.Equals(flag.Status, "pending", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new ErrorResponse("Clue flag has already been reviewed"));

            var status = body.Status.Trim().ToLowerInvariant();
            if (status is not ("approved" or "rejected"))
                return Results.BadRequest(new ErrorResponse("Status must be approved or rejected"));

            string? resolvedClue = null;
            string? wordListVersion = null;
            if (status == "approved")
            {
                var candidateClue = string.IsNullOrWhiteSpace(body.UpdatedClue)
                    ? flag.SuggestedClue
                    : body.UpdatedClue;

                if (body.RemoveClue)
                {
                    candidateClue = null;
                }
                else if (string.IsNullOrWhiteSpace(candidateClue))
                {
                    return Results.BadRequest(new ErrorResponse("Updated clue is required when approving"));
                }

                var update = wordListService.UpdateClueInOriginFile(
                    flag.Word,
                    flag.CurrentClue,
                    candidateClue,
                    body.ExpectedWordListVersion,
                    body.RemoveClue);
                if (update.Result == WordListUpdateResult.NotFound)
                    return Results.NotFound(new ErrorResponse($"Word '{flag.Word}' with clue '{flag.CurrentClue}' was not found in source word files"));
                if (update.Result == WordListUpdateResult.VersionConflict)
                    return Results.Conflict(new { error = "Word list changed since last read", currentVersion = update.CurrentVersion, source = update.SourceKey });

                resolvedClue = body.RemoveClue ? null : candidateClue?.Trim();
                wordListVersion = update.CurrentVersion;

                dictionary.Reload();
                generator.RebuildWordAnalysisCache();
                await warmup.RegenerateFutureAsync(ct);
            }

            var resolved = await clueFlagStore.ResolveClueFlagAsync(id, status, resolvedClue, body.AdminNote, adminUserId);
            if (!resolved)
                return Results.Conflict(new ErrorResponse("Clue flag could not be resolved"));

            return Results.Ok(new { ok = true, wordListVersion });
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
