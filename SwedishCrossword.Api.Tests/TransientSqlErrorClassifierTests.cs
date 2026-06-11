using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

[Category("Unit")]
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
    [Test, Category("Unit"), Category("Validation"), Category("Smoke")]
    [Arguments(40613, "Database resuming from auto-pause", DisplayName = "Transient code 40613")]
    [Arguments(42108, "Login resuming", DisplayName = "Transient code 42108")]
    [Arguments(42109, "Server starting up", DisplayName = "Transient code 42109")]
    [Arguments(42119, "Free Offer DB paused after monthly quota exhausted", DisplayName = "Transient code 42119")]
    [Arguments(49918, "Cannot process request — not enough resources", DisplayName = "Transient code 49918")]
    [Arguments(49919, "Cannot process create/update request", DisplayName = "Transient code 49919")]
    [Arguments(49920, "Cannot process delete request", DisplayName = "Transient code 49920")]
    [Arguments(40197, "Service encountered an error processing the request", DisplayName = "Transient code 40197")]
    [Arguments(40501, "Service is currently busy", DisplayName = "Transient code 40501")]
    [Arguments(10928, "Resource limit reached", DisplayName = "Transient code 10928")]
    [Arguments(10929, "Resource limit reached", DisplayName = "Transient code 10929")]
    [Arguments(10053, "Connection forcibly closed", DisplayName = "Transient code 10053")]
    [Arguments(10054, "Connection reset", DisplayName = "Transient code 10054")]
    [Arguments(10060, "Connection timed out", DisplayName = "Transient code 10060")]
    [Arguments(1205, "Deadlock victim", DisplayName = "Transient code 1205")]
    [Arguments(4060, "Cannot open database (often during resume)", DisplayName = "Transient code 4060")]
    [Arguments(233, "Pre-login handshake failure", DisplayName = "Transient code 233")]
    [Arguments(64, "Network name no longer available", DisplayName = "Transient code 64")]
    [Arguments(-2, "Client-side command/login timeout", DisplayName = "Transient code -2")]
    public async Task TransientErrorNumbers_ContainsKnownTransientCode(int code, string description)
    {
        await Assert.That(TransientSqlErrorClassifier.TransientErrorNumbers.Contains(code))
            .IsTrue()
            .Because($"SQL error {code} ({description}) must be treated as transient — see TransientSqlErrorClassifier.cs");
    }

    // Codes that MUST NOT be in the set — these represent application bugs
    // or user errors and should bubble up as 500/400, not be hidden as 503.
    [Test, Category("Unit"), Category("Validation"), Category("Smoke")]
    [Arguments(18456, "Login failed for user — bad credentials/permissions", DisplayName = "Non-transient code 18456")]
    [Arguments(8152, "String or binary data would be truncated — schema/data bug", DisplayName = "Non-transient code 8152")]
    [Arguments(2627, "Unique constraint violation — caller error", DisplayName = "Non-transient code 2627")]
    [Arguments(547, "Foreign key constraint violation — caller error", DisplayName = "Non-transient code 547")]
    [Arguments(2601, "Cannot insert duplicate key — caller error", DisplayName = "Non-transient code 2601")]
    public async Task TransientErrorNumbers_DoesNotContainNonTransientCode(int code, string description)
    {
        await Assert.That(TransientSqlErrorClassifier.TransientErrorNumbers.Contains(code))
            .IsFalse()
            .Because($"SQL error {code} ({description}) is not transient and must not be masked as 503");
    }
}
