using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SwedishCrossword.Api;

/// <summary>
/// Translates transient/unavailable Azure SQL exceptions into a clean
/// HTTP 503 response (problem+json) instead of a 500. Lets the front-end
/// distinguish "DB temporarily down" from real server errors and degrade
/// gracefully (e.g. show a banner, keep puzzle play working).
/// Returns <c>false</c> for non-SQL or non-transient exceptions so the
/// existing fallback handler still records them as 500s.
/// </summary>
internal sealed class TransientDbExceptionHandler(ILogger<TransientDbExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not SqlException sqlEx) return false;
        if (!TransientSqlErrorClassifier.IsTransient(sqlEx)) return false;

        // 42119 = Free Offer DB paused after monthly quota exhausted (not auto-resume).
        // Worth distinguishing in logs because retries won't help until the next month.
        var isQuotaPaused = false;
        foreach (SqlError err in sqlEx.Errors)
            if (err.Number == 42119) { isQuotaPaused = true; break; }

        var safeMethod = (httpContext.Request.Method ?? string.Empty).Replace("\r", "").Replace("\n", "");
        var safePath = (httpContext.Request.Path.Value ?? string.Empty).Replace("\r", "").Replace("\n", "");

        // Warning, not Error — this is expected during DB cold-start / quota reset
        // and should not page anyone or pollute Application Insights failure rates.
        logger.LogWarning(sqlEx,
            "Database unavailable on {Method} {Path} (SQL error {Number}, quotaPaused={QuotaPaused})",
            safeMethod, safePath, sqlEx.Number, isQuotaPaused);

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = "30";
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.io/503",
            title = "Databasen är tillfälligt otillgänglig",
            status = 503,
            code = "db_unavailable",
            detail = "Försök igen om en stund. Pussel fungerar som vanligt."
        }, cancellationToken: cancellationToken);

        return true;
    }
}
