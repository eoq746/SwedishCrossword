using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace SwedishCrossword.Api;

internal static class AuthEndpoints
{
    /// <summary>
    /// Derives a stable, opaque user identifier from the authentication provider
    /// and the provider's unique subject/nameidentifier claim.
    /// Returns null when the user is not authenticated.
    /// </summary>
    internal static string? GetUserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var provider = user.Identity.AuthenticationType ?? "unknown";
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub))
            return null;

        // SHA256 so we never store raw provider IDs
        var raw = Encoding.UTF8.GetBytes($"{provider}:{sub}");
        var hash = SHA256.HashData(raw);
        return Convert.ToHexStringLower(hash);
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

        app.MapGet("/api/auth/me", async (ClaimsPrincipal user, LeaderboardStore store) =>
        {
            if (user.Identity?.IsAuthenticated != true)
                return Results.Json(new { authenticated = false });

            var name = user.FindFirstValue(ClaimTypes.Name)
                       ?? user.FindFirstValue("name")
                       ?? "Okänd";

            var email = user.FindFirstValue(ClaimTypes.Email);
            var avatarUrl = user.FindFirstValue("picture")
                           ?? user.FindFirstValue("urn:google:picture");

            var userId = GetUserId(user);
            var alias = userId is not null ? await store.GetAliasAsync(userId) : null;

            return Results.Json(new
            {
                authenticated = true,
                userId,
                name,
                alias,
                email,
                avatarUrl,
                provider = user.Identity.AuthenticationType
            });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { loggedOut = true });
        });

        app.MapGet("/api/auth/my-stats", async (ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var stats = await store.GetUserStatsAsync(userId);
            return Results.Ok(stats);
        }).RequireAuthorization();

        app.MapGet("/api/auth/alias", async (ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var alias = await store.GetAliasAsync(userId);
            return Results.Ok(new { alias });
        }).RequireAuthorization();

        app.MapPut("/api/auth/alias", async (AliasRequest body, ClaimsPrincipal user, LeaderboardStore store) =>
        {
            var userId = GetUserId(user);
            if (userId is null)
                return Results.Json(new ErrorResponse("Not authenticated"), statusCode: 401);

            var alias = LeaderboardStore.SanitiseName(body.Alias);
            if (string.IsNullOrWhiteSpace(alias) || alias.Length < 2 || alias.Length > 20)
                return Results.BadRequest(new ErrorResponse("Alias måste vara 2–20 tecken"));

            if (!await store.IsAliasAvailableAsync(alias, excludeUserId: userId))
                return Results.Conflict(new ErrorResponse("Aliaset är redan taget"));

            await store.SetAliasAsync(userId, alias);
            return Results.Ok(new { alias });
        }).RequireAuthorization();

        return app;
    }
}
