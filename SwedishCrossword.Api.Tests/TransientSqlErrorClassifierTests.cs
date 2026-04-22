using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

/// <summary>
/// Regression tests for the centralised transient SQL error set.
/// Constructing a real <c>SqlException</c> is impractical (internal ctors),
/// so behaviour of <see cref="TransientDbExceptionHandler"/> is verified
/// indirectly by asserting the classifier knows about the right error codes.
/// </summary>
public class TransientSqlErrorClassifierTests
{
    // Codes that MUST stay in the set — each one corresponds to a real
    // production failure mode we want to convert to a 503 (not 500).
    [Test]
    [Arguments(40613, "Database resuming from auto-pause")]
    [Arguments(42108, "Login resuming")]
    [Arguments(42109, "Server starting up")]
    [Arguments(42119, "Free Offer DB paused after monthly quota exhausted")]
    [Arguments(49918, "Cannot process request — not enough resources")]
    [Arguments(49919, "Cannot process create/update request")]
    [Arguments(49920, "Cannot process delete request")]
    [Arguments(40197, "Service encountered an error processing the request")]
    [Arguments(40501, "Service is currently busy")]
    [Arguments(10928, "Resource limit reached")]
    [Arguments(10929, "Resource limit reached")]
    [Arguments(10053, "Connection forcibly closed")]
    [Arguments(10054, "Connection reset")]
    [Arguments(10060, "Connection timed out")]
    [Arguments(1205, "Deadlock victim")]
    [Arguments(4060, "Cannot open database (often during resume)")]
    [Arguments(233, "Pre-login handshake failure")]
    [Arguments(64, "Network name no longer available")]
    [Arguments(-2, "Client-side command/login timeout")]
    public async Task TransientErrorNumbers_ContainsKnownTransientCode(int code, string description)
    {
        await Assert.That(TransientSqlErrorClassifier.TransientErrorNumbers.Contains(code))
            .IsTrue()
            .Because($"SQL error {code} ({description}) must be treated as transient — see TransientSqlErrorClassifier.cs");
    }

    // Codes that MUST NOT be in the set — these represent application bugs
    // or user errors and should bubble up as 500/400, not be hidden as 503.
    [Test]
    [Arguments(18456, "Login failed for user — bad credentials/permissions")]
    [Arguments(8152, "String or binary data would be truncated — schema/data bug")]
    [Arguments(2627, "Unique constraint violation — caller error")]
    [Arguments(547, "Foreign key constraint violation — caller error")]
    [Arguments(2601, "Cannot insert duplicate key — caller error")]
    public async Task TransientErrorNumbers_DoesNotContainNonTransientCode(int code, string description)
    {
        await Assert.That(TransientSqlErrorClassifier.TransientErrorNumbers.Contains(code))
            .IsFalse()
            .Because($"SQL error {code} ({description}) is not transient and must not be masked as 503");
    }
}
