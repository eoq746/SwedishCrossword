using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SwedishCrossword.Api.Endpoints;

internal static class AuthEndpoints
{
    internal readonly record struct UserIdentity(string UserId, string? LegacyUserId);

    private static string HashUserId(string provider, string subject)
    {
        var raw = Encoding.UTF8.GetBytes($"{provider}:{subject}");
        var hash = SHA256.HashData(raw);
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsLocalIssuer(string issuer)
    {
        return string.Equals(issuer, "LOCAL AUTHORITY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(issuer, "LOCAL_AUTHORITY", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetCanonicalProvider(ClaimsPrincipal user)
    {
        var identityProvider = user.FindFirstValue("iss")
                            ?? user.FindFirstValue("idp")
                            ?? user.FindFirstValue(ClaimTypes.AuthenticationMethod);
        if (!string.IsNullOrWhiteSpace(identityProvider))
            return identityProvider.Trim().ToLowerInvariant();

        var subClaim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
        if (!string.IsNullOrWhiteSpace(subClaim?.Issuer) && !IsLocalIssuer(subClaim.Issuer))
            return subClaim.Issuer.Trim().ToLowerInvariant();

        var authType = user.Identity?.AuthenticationType;
        return string.IsNullOrWhiteSpace(authType) ? null : authType.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Derives canonical and legacy opaque user identifiers from provider + subject claims.
    /// Canonical ID prefers issuer-based provider identity to avoid cross-provider collisions.
    /// </summary>
    internal static UserIdentity? GetUserIdentity(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var canonicalProvider = GetCanonicalProvider(user) ?? "unknown";
        var canonicalUserId = HashUserId(canonicalProvider, subject);

        var legacyProvider = user.Identity.AuthenticationType ?? "unknown";
        var legacyUserId = HashUserId(legacyProvider, subject);

        return string.Equals(canonicalUserId, legacyUserId, StringComparison.Ordinal)
            ? new UserIdentity(canonicalUserId, null)
            : new UserIdentity(canonicalUserId, legacyUserId);
    }

    /// <summary>
    /// Returns the canonical opaque user identifier for the authenticated user.
    /// </summary>
    internal static string? GetUserId(ClaimsPrincipal user) => GetUserIdentity(user)?.UserId;

    /// <summary>
    /// Returns the legacy opaque user identifier when it differs from canonical derivation.
    /// </summary>
    internal static string? GetLegacyUserId(ClaimsPrincipal user) => GetUserIdentity(user)?.LegacyUserId;

    /// <summary>
    /// Resolves the authenticated user to canonical ID and migrates legacy rows when needed.
    /// </summary>
    internal static async Task<string?> ResolveUserIdAsync(ClaimsPrincipal user, IUserProfileStore store)
    {
        var identity = GetUserIdentity(user);
        if (identity is null)
            return null;

        return await store.ResolveCanonicalUserIdAsync(identity.Value.UserId, identity.Value.LegacyUserId);
    }

    internal static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/login/{provider}", async (string provider, string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            var scheme = provider.ToLowerInvariant() switch
            {
                "google" => "Google",
                "microsoft" => "Microsoft",
                _ => null
            };

            if (scheme is null)
                return Results.BadRequest(new ErrorResponse("Unsupported provider"));

            // Verify the scheme is actually registered (provider credentials may be missing)
            var schemeInfo = await schemes.GetSchemeAsync(scheme);
            if (schemeInfo is null)
                return Results.BadRequest(new ErrorResponse($"Provider '{provider}' is not configured"));

            // Prevent open-redirect attacks — only allow local redirects
            var redirect = Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && !string.IsNullOrWhiteSpace(returnUrl)
                ? returnUrl
                : "/";
            var properties = new AuthenticationProperties { RedirectUri = redirect };
            return Results.Challenge(properties, [scheme]);
        });

        app.MapGet("/api/auth/me", async (ClaimsPrincipal user, IUserProfileStore store, IAdminStore adminStore, IConfiguration config, ILoggerFactory loggerFactory) =>
        {
            if (user.Identity?.IsAuthenticated != true)
                return Results.Json(new { authenticated = false });

            var name = user.FindFirstValue(ClaimTypes.Name)
                       ?? user.FindFirstValue("name")
                       ?? "Okänd";

            var avatarUrl = user.FindFirstValue("picture")
                           ?? user.FindFirstValue("urn:google:picture");

            var userIdentity = GetUserIdentity(user);
            var userId = userIdentity?.UserId;

            // Authentication state lives in the cookie, not the DB. Don't fail
            // the whole endpoint if the alias lookup throws (e.g. SQL paused
            // or temporarily unavailable) — the user is still logged in.
            string? alias = null;
            var aliasUnavailable = false;
            if (userId is not null)
            {
                try
                {
                    userId = await store.ResolveCanonicalUserIdAsync(userId, userIdentity?.LegacyUserId);
                    alias = await store.GetAliasAsync(userId);
                }
                catch (Exception ex)
                {
                    aliasUnavailable = true;
                    loggerFactory.CreateLogger("AuthEndpoints")
                        .LogWarning(ex, "Alias lookup failed for user {UserId} — returning authenticated state without alias", userId);
                }
            }

            var adminIds = config.GetSection("Authorization:AdminUserIds").Get<string[]>() ?? [];
            var isAdmin = userId is not null &&
                          (adminIds.Contains(userId)
                           || (userIdentity?.LegacyUserId is not null && adminIds.Contains(userIdentity.Value.LegacyUserId))
                           || await adminStore.IsAdminAsync(userId));

            return Results.Json(new
            {
                authenticated = true,
                userId,
                name,
                alias,
                aliasUnavailable,
                avatarUrl,
                provider = user.Identity.AuthenticationType,
                isAdmin
            });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { loggedOut = true });
        });

        app.MapGet("/api/auth/my-stats", async (ClaimsPrincipal user, IUserProfileStore store) =>
        {
            var userIdentity = GetUserIdentity(user);
            if (userIdentity is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var userId = await store.ResolveCanonicalUserIdAsync(userIdentity.Value.UserId, userIdentity.Value.LegacyUserId);
            var stats = await store.GetUserStatsAsync(userId);
            return Results.Ok(stats);
        }).RequireAuthorization();

        app.MapGet("/api/auth/alias", async (ClaimsPrincipal user, IUserProfileStore store) =>
        {
            var userIdentity = GetUserIdentity(user);
            if (userIdentity is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var userId = await store.ResolveCanonicalUserIdAsync(userIdentity.Value.UserId, userIdentity.Value.LegacyUserId);
            var alias = await store.GetAliasAsync(userId);
            return Results.Ok(new { alias });
        }).RequireAuthorization();

        app.MapPut("/api/auth/alias", async (AliasRequest body, ClaimsPrincipal user, IUserProfileStore store) =>
        {
            var userIdentity = GetUserIdentity(user);
            if (userIdentity is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var userId = await store.ResolveCanonicalUserIdAsync(userIdentity.Value.UserId, userIdentity.Value.LegacyUserId);
            var alias = LeaderboardStore.SanitiseName(body.Alias);
            if (string.IsNullOrWhiteSpace(alias) || alias.Length < 2 || alias.Length > 20)
                return Results.BadRequest(new ErrorResponse("Alias måste vara 2–20 tecken"));

            if (!await store.IsAliasAvailableAsync(alias, excludeUserId: userId))
                return Results.Conflict(new ErrorResponse("Aliaset är redan taget"));

            var set = await store.SetAliasAsync(userId, alias);
            if (!set)
                return Results.Conflict(new ErrorResponse("Aliaset är redan taget"));
            return Results.Ok(new { alias });
        }).RequireAuthorization();

        // GDPR Art. 20: Data portability — export all personal data
        app.MapGet("/api/auth/my-data", async (ClaimsPrincipal user, IUserProfileStore store) =>
        {
            var userIdentity = GetUserIdentity(user);
            if (userIdentity is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var userId = await store.ResolveCanonicalUserIdAsync(userIdentity.Value.UserId, userIdentity.Value.LegacyUserId);
            var export = await store.ExportUserDataAsync(userId);
            return Results.Ok(export);
        }).RequireAuthorization();

        // GDPR Art. 17: Right to erasure — delete all personal data
        app.MapDelete("/api/auth/account", async (ClaimsPrincipal user, IUserProfileStore store, HttpContext ctx) =>
        {
            var userIdentity = GetUserIdentity(user);
            if (userIdentity is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var userId = await store.ResolveCanonicalUserIdAsync(userIdentity.Value.UserId, userIdentity.Value.LegacyUserId);
            await store.DeleteUserDataAsync(userId);
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { deleted = true });
        }).RequireAuthorization();

        return app;
    }
}
