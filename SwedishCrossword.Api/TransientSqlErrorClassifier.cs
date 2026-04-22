using Microsoft.Data.SqlClient;

namespace SwedishCrossword.Api;

/// <summary>
/// Centralised classification of Azure SQL error numbers that should be treated
/// as "database temporarily unavailable" — covers transient connection failures,
/// throttling, deadlocks, and the Free Offer auto-pause/quota states.
/// Single source of truth shared by <see cref="LeaderboardStore"/> (for retry)
/// and <see cref="TransientDbExceptionHandler"/> (for the 503 response).
/// </summary>
internal static class TransientSqlErrorClassifier
{
    // 40613/42108/42109/42119/49918/49919/49920 = database/login resuming or unavailable
    //   (42119 is also raised when a Free Offer DB is paused after exhausting its
    //    monthly vCore-second quota).
    // 40197/40501/10928/10929 = service busy/throttled.
    // 10053/10054/10060 = network drop. 1205 = deadlock.
    // 4060 = cannot open db (often during resume). 233/64 = pre-login.
    // -2 = client-side command/login timeout.
    internal static readonly HashSet<int> TransientErrorNumbers = new()
    {
        -2, 64, 233, 1205, 4060,
        10053, 10054, 10060, 10928, 10929,
        40197, 40501, 40613,
        42108, 42109, 42119,
        49918, 49919, 49920
    };

    internal static bool IsTransient(SqlException ex)
    {
        foreach (SqlError err in ex.Errors)
            if (TransientErrorNumbers.Contains(err.Number)) return true;
        return false;
    }
}
