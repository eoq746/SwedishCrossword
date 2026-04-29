using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using SwedishCrossword.Api;
using SwedishCrossword.Api.Endpoints;
using SwedishCrossword.Services;

var builder = WebApplication.CreateBuilder(args);

// Limit request body size globally (no endpoint needs more than 100 KB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 100 * 1024;
});

// Register domain services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SwedishDictionary>();
builder.Services.AddSingleton<GridValidator>();
builder.Services.AddSingleton<ClueGenerator>();
builder.Services.AddSingleton<CrosswordGenerator>();
builder.Services.AddSingleton<PrintService>();
builder.Services.AddSingleton<LeaderboardStore>();
// Expose LeaderboardStore through narrow interfaces so consumers depend only on the
// surface area they actually use. Backed by the same singleton instance for now;
// implementations can be split into separate classes later without touching callers.
builder.Services.AddSingleton<IScoreStore>(sp => sp.GetRequiredService<LeaderboardStore>());
builder.Services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<LeaderboardStore>());
builder.Services.AddSingleton<IUserProfileStore>(sp => sp.GetRequiredService<LeaderboardStore>());
builder.Services.AddSingleton<IFriendStore>(sp => sp.GetRequiredService<LeaderboardStore>());
builder.Services.AddSingleton<IAnalyticsStore>(sp => sp.GetRequiredService<LeaderboardStore>());
builder.Services.AddSingleton<SubmissionTokenService>();
builder.Services.AddSingleton<PuzzleCache>();
builder.Services.AddSingleton<PuzzleDateIndex>();

// Background service: pre-generates today's puzzle at startup so the first visitor never waits
builder.Services.AddHostedService<PuzzleWarmupService>();

// Background service: periodically prunes old leaderboard entries (removes work from write path)
builder.Services.AddHostedService<LeaderboardPruneService>();

var trustedProxyValues = builder.Configuration.GetSection("ForwardedHeaders:TrustedProxies").Get<string[]>() ?? [];
var trustedNetworkValues = builder.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>() ?? [];

var trustedProxies = new List<IPAddress>(trustedProxyValues.Length);
foreach (var value in trustedProxyValues)
{
    if (string.IsNullOrWhiteSpace(value))
        continue;
    if (!IPAddress.TryParse(value, out var ip))
        throw new ArgumentException($"Invalid ForwardedHeaders:TrustedProxies entry '{value}'");
    trustedProxies.Add(ip);
}

static System.Net.IPNetwork ParseTrustedNetwork(string value)
{
    if (!System.Net.IPNetwork.TryParse(value, out var network))
        throw new ArgumentException($"Invalid ForwardedHeaders:TrustedNetworks entry '{value}'", nameof(value));

    return network;
}

var trustedNetworks = new List<System.Net.IPNetwork>(trustedNetworkValues.Length);
foreach (var value in trustedNetworkValues)
{
    if (string.IsNullOrWhiteSpace(value))
        continue;
    trustedNetworks.Add(ParseTrustedNetwork(value));
}

if (!builder.Environment.IsDevelopment() && trustedProxies.Count == 0 && trustedNetworks.Count == 0)
{
    throw new InvalidOperationException(
        "Forwarded headers trust is not configured. Set ForwardedHeaders:TrustedProxies and/or " +
        "ForwardedHeaders:TrustedNetworks in non-Development environments.");
}

// Forwarded headers — trust only configured proxies/networks
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.RequireHeaderSymmetry = true;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var proxy in trustedProxies)
        options.KnownProxies.Add(proxy);

    foreach (var network in trustedNetworks)
        options.KnownIPNetworks.Add(network);  // Changed from KnownNetworks to KnownIPNetworks
});

// Health checks
builder.Services.AddHealthChecks();

// Translate transient/unavailable Azure SQL errors (incl. Free Offer 42119
// quota-pause) into HTTP 503 problem+json instead of 500. Lets the front-end
// distinguish "DB temporarily down" from real server errors.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TransientDbExceptionHandler>();

// Authentication — cookie-based with Google & Microsoft social login
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = ".Crossword.Auth";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    // API endpoints: return 401 instead of redirect (ASP.NET Core 10 does this automatically for minimal APIs)
    options.LoginPath = null;
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        // GDPR data minimisation: only request the profile scope (name + picture),
        // not the email scope which is included by default.
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
    });
}

var microsoftClientId = builder.Configuration["Authentication:Microsoft:ClientId"];
var microsoftClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"];
if (!string.IsNullOrEmpty(microsoftClientId) && !string.IsNullOrEmpty(microsoftClientSecret))
{
    authBuilder.AddMicrosoftAccount(options =>
    {
        options.ClientId = microsoftClientId;
        options.ClientSecret = microsoftClientSecret;
    });
}

builder.Services.AddAuthorization(options =>
{
    var adminIds = builder.Configuration.GetSection("Authorization:AdminUserIds").Get<string[]>() ?? [];
    options.AddPolicy("Admin", policy =>
        policy.RequireAssertion(context =>
        {
            var userId = AuthEndpoints.GetUserId(context.User);
            return userId is not null && adminIds.Contains(userId);
        }));
});

// Data Protection — persist keys to the shared data volume so auth cookies
// survive container restarts and new revisions in Azure Container Apps
var dataProtectionPath = builder.Configuration["Storage:LeaderboardPath"];
if (string.IsNullOrWhiteSpace(dataProtectionPath))
    dataProtectionPath = Path.Combine(AppContext.BaseDirectory, "leaderboard");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataProtectionPath, "keys")))
    .SetApplicationName("SwedishCrossword");

// Output caching — avoids redundant disk reads for puzzle endpoints
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("puzzle-today", p => p.Expire(TimeSpan.FromMinutes(5)));
    options.AddPolicy("puzzle-archive", p => p.Expire(TimeSpan.FromHours(1)));
    options.AddPolicy("puzzle-dates", p => p.Expire(TimeSpan.FromMinutes(10)));
});

// Response compression — Brotli + Gzip for JSON and static assets
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// OpenAPI documentation
builder.Services.AddOpenApi();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global per-IP rate limit for all endpoints
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString()
                ?? context.Connection.Id
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 200,
                QueueLimit = 0
            }));

    options.AddFixedWindowLimiter("leaderboard-write", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("puzzle-interact", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("friends", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 30;
        opt.QueueLimit = 0;
    });
});

// CORS — configurable via Cors:AllowedOrigins in appsettings (use ["*"] to allow all)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 } && allowedOrigins.Contains("*"))
        {
            // Wildcard: allow any origin but WITHOUT credentials for security.
            // Use explicit origins if you need cookies/auth across origins.
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        // else: no origins configured — default policy allows nothing
    });
});

var app = builder.Build();

// Resolve configurable storage paths (override via env vars: Storage__PuzzlePath, Storage__LeaderboardPath)
var puzzlePath = app.Configuration["Storage:PuzzlePath"];
if (string.IsNullOrWhiteSpace(puzzlePath))
    puzzlePath = Path.Combine(AppContext.BaseDirectory, "puzzles");
Directory.CreateDirectory(puzzlePath);

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // UseExceptionHandler invokes registered IExceptionHandler instances first
    // (here: TransientDbExceptionHandler returns 503 for transient SQL errors).
    // If none handle the exception, the fallback delegate below logs + returns 500.
    app.UseExceptionHandler(err => err.Run(async ctx =>
    {
        var exFeature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (exFeature?.Error is { } ex)
        {
            var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("UnhandledException");
            var safeMethod = (ctx.Request.Method ?? string.Empty).Replace("\r", "").Replace("\n", "");
            var safePath = (ctx.Request.Path.Value ?? string.Empty).Replace("\r", "").Replace("\n", "");
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", safeMethod, safePath);
        }

        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"error\":\"An unexpected error occurred\"}");
    }));
    app.UseHttpsRedirection();
}

static string GenerateCspNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

static string GetCspNonce(HttpContext context)
{
    if (context.Items.TryGetValue("CspNonce", out var value) && value is string nonce && !string.IsNullOrEmpty(nonce))
        return nonce;
    throw new InvalidOperationException("Missing CSP nonce in request context.");
}

static string? TryResolveHtmlFilePath(string webRootPath, PathString path)
{
    var pathValue = path.Value;

    // SPA fallback: any path under /app/ with no file extension → serve the React shell.
    // Paths with an extension (e.g. /app/assets/main.js) are static assets that must be
    // served by UseStaticFiles; return null so they fall through.
    if (pathValue is not null && pathValue.StartsWith("/app", StringComparison.OrdinalIgnoreCase))
    {
        var lastDot = pathValue.AsSpan().LastIndexOf('.');
        var lastSlash = pathValue.AsSpan().LastIndexOf('/');
        if (lastDot > lastSlash)
            return null; // has an extension → static asset

        var spaIndex = Path.Combine(webRootPath, "app", "index.html");
        return File.Exists(spaIndex) ? spaIndex : null;
    }

    var relative = pathValue switch
    {
        "/" or "" => "index.html",
        var p when p is not null && p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) => p.TrimStart('/'),
        _ => null
    };

    if (string.IsNullOrEmpty(relative) || relative.Contains("..", StringComparison.Ordinal))
        return null;

    var candidate = Path.Combine(webRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
    return File.Exists(candidate) ? candidate : null;
}

var isProduction = !app.Environment.IsDevelopment();

// Security headers — registered BEFORE OutputCache so cached responses include them.
// On a cache hit, OutputCache short-circuits and skips downstream middleware,
// so anything that must be present on every response (including cached ones)
// must run before UseOutputCache().
app.Use(async (context, next) =>
{
    var cspNonce = GenerateCspNonce();
    context.Items["CspNonce"] = cspNonce;

    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers.ContentSecurityPolicy =
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{cspNonce}' https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://*.google.com https://*.googlesyndication.com https://*.googletagservices.com https://cdn.jsdelivr.net; " +
        $"style-src 'self' 'unsafe-inline'; " +
        $"style-src-elem 'self' 'nonce-{cspNonce}'; " +
        "style-src-attr 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self' https://pagead2.googlesyndication.com https://*.google.com https://*.googlesyndication.com https://*.adtrafficquality.google; " +
        "font-src 'self' data:; " +
        "frame-src 'self' https://googleads.g.doubleclick.net https://*.google.com https://*.googlesyndication.com; " +
        "worker-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self' https://accounts.google.com https://login.microsoftonline.com";
    if (isProduction)
        headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    await next();
});

static bool IsSameOriginHeader(string? headerValue, HttpContext context)
{
    if (string.IsNullOrWhiteSpace(headerValue) || string.Equals(headerValue, "null", StringComparison.OrdinalIgnoreCase))
        return false;

    if (!Uri.TryCreate(headerValue, UriKind.Absolute, out var uri))
        return false;

    var requestScheme = context.Request.Scheme;
    if (!string.Equals(uri.Scheme, requestScheme, StringComparison.OrdinalIgnoreCase))
        return false;

    if (!string.Equals(uri.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        return false;

    var expectedPort = context.Request.Host.Port
        ?? (string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
    var sourcePort = uri.IsDefaultPort
        ? (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
        : uri.Port;

    return sourcePort == expectedPort;
}

// CSRF protection: require same-origin Origin/Referer on state-changing requests
// that carry the auth cookie.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) || HttpMethods.IsPatch(method))
    {
        // CSRF is only relevant when browser credentials are auto-sent.
        // For this app that means the auth cookie flow.
        if (context.Request.Cookies.ContainsKey(".Crossword.Auth"))
        {
            var origin = context.Request.Headers.Origin.FirstOrDefault();
            var referer = context.Request.Headers.Referer.FirstOrDefault();
            var valid = IsSameOriginHeader(origin, context) || IsSameOriginHeader(referer, context);
            if (!valid)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("{\"error\":\"CSRF validation failed\"}");
                return;
            }
        }
    }
    await next();
});

app.UseCors();

var webRootPath = app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
app.Use(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
    {
        await next();
        return;
    }

    var filePath = TryResolveHtmlFilePath(webRootPath, context.Request.Path);
    if (filePath is null)
    {
        await next();
        return;
    }

    var html = await File.ReadAllTextAsync(filePath, context.RequestAborted);
    var nonce = GetCspNonce(context);
    // Expose the nonce to JavaScript so scripts that create <style> elements
    // at runtime (e.g. cookie-consent.js) can stamp it and satisfy style-src-elem.
    // Injected as the very first child of <head> so it runs before any other script.
    var nonceScript = $"<script nonce=\"{nonce}\">window.__cspNonce__='{nonce}';</script>";
    var content = html
        .Replace("__CSP_NONCE__", nonce, StringComparison.Ordinal)
        .Replace("<head>", $"<head>{nonceScript}", StringComparison.OrdinalIgnoreCase);
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(content, context.RequestAborted);
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var requestPath = ctx.Context.Request.Path.Value ?? string.Empty;
        var ext = Path.GetExtension(ctx.File.Name).ToLowerInvariant();

        if (ext is ".css" or ".js" or ".png" or ".ico" or ".webmanifest")
        {
            // Vite-built assets under /app/assets/ embed a content hash in their filename
            // (e.g. index-Cv6bXOb_.css). The hash changes whenever the content changes,
            // so immutable caching is safe — there is zero stale-cache risk.
            //
            // All other static files (tokens.css, site.min.css, images, manifest, …) are
            // NOT content-hashed. Caching them as immutable means edits never reach
            // returning visitors until their 7-day cache entry expires. Use no-cache so
            // the browser validates with the server on every navigation; a 304 Not Modified
            // response still avoids re-downloading unchanged bytes.
            var isVersionedAsset = requestPath.StartsWith("/app/assets/", StringComparison.OrdinalIgnoreCase);
            ctx.Context.Response.Headers.CacheControl = isVersionedAsset
                ? "public, max-age=604800, immutable"
                : "no-cache";
        }
    }
});

app.UseAuthentication();

app.UseAuthorization();
app.UseRateLimiter();
app.UseResponseCompression();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAuthEndpoints();
app.MapPuzzleEndpoints(puzzlePath);
app.MapLeaderboardEndpoints();
app.MapStatsEndpoints();
app.MapAnalyticsEndpoints();
app.MapFriendsEndpoints();

app.MapHealthChecks("/api/health");

// Dynamic sitemap that includes all puzzle dates for better SEO indexing
app.MapGet("/sitemap-puzzles.xml", (PuzzleDateIndex dateIndex, TimeProvider timeProvider) =>
{
    var today = timeProvider.GetSwedishDate();
    var dates = dateIndex.GetDates(today);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
    sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");
    foreach (var entry in dates)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"  <url><loc>https://www.svensktkorsord.se/puzzle.html?date={entry.Date}</loc><changefreq>never</changefreq><priority>0.6</priority></url>");
    }
    sb.AppendLine("</urlset>");
    return Results.Content(sb.ToString(), "application/xml; charset=utf-8");
}).ExcludeFromDescription();

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests
