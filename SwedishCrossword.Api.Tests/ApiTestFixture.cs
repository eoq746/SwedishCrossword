using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace SwedishCrossword.Api.Tests;

/// <summary>
/// Reusable test fixture for API integration tests to reduce factory creation overhead.
/// Provides isolated storage paths while reusing a single application instance.
/// </summary>
internal class ApiTestFixture : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _tempPuzzlePath;
    private readonly string _tempLeaderboardPath;
    private readonly HttpClient _client;

    public HttpClient Client => _client;
    public string TempPuzzlePath => _tempPuzzlePath;
    public string TempLeaderboardPath => _tempLeaderboardPath;
    public WebApplicationFactory<Program> Factory => _factory;

    /// <summary>
    /// Creates a fixture with auto-generated temp paths.
    /// </summary>
    public ApiTestFixture()
    {
        _tempPuzzlePath = Path.Combine(Path.GetTempPath(), "sc-test-puzzles-" + Guid.NewGuid());
        _tempLeaderboardPath = Path.Combine(Path.GetTempPath(), "sc-test-lb-" + Guid.NewGuid());
        _factory = CreateFactory(_tempPuzzlePath, _tempLeaderboardPath, enableTestAuth: false);
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Creates a fixture with auto-generated temp paths and optional test auth.
    /// </summary>
    public ApiTestFixture(bool enableTestAuth)
    {
        _tempPuzzlePath = Path.Combine(Path.GetTempPath(), "sc-test-puzzles-" + Guid.NewGuid());
        _tempLeaderboardPath = Path.Combine(Path.GetTempPath(), "sc-test-lb-" + Guid.NewGuid());
        _factory = CreateFactory(_tempPuzzlePath, _tempLeaderboardPath, enableTestAuth);
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// Creates a fixture with a custom leaderboard path and auto-generated puzzle path.
    /// Useful for testing migrations where the leaderboard directory already exists.
    /// </summary>
    public ApiTestFixture(string customLeaderboardPath)
    {
        _tempPuzzlePath = Path.Combine(Path.GetTempPath(), "sc-test-puzzles-" + Guid.NewGuid());
        _tempLeaderboardPath = customLeaderboardPath;
        _factory = CreateFactory(_tempPuzzlePath, _tempLeaderboardPath, enableTestAuth: false);
        _client = _factory.CreateClient();
    }

    private static WebApplicationFactory<Program> CreateFactory(string puzzlePath, string leaderboardPath, bool enableTestAuth)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Storage:PuzzlePath", puzzlePath);
                builder.UseSetting("Storage:LeaderboardPath", leaderboardPath);
                builder.ConfigureServices(services =>
                {
                    var hostedServiceRegistrations = services
                        .Where(d => d.ServiceType == typeof(IHostedService))
                        .ToList();

                    foreach (var registration in hostedServiceRegistrations)
                    {
                        services.Remove(registration);
                    }

                    if (enableTestAuth)
                    {
                        services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                            options.DefaultScheme = TestAuthHandler.SchemeName;
                        }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                            TestAuthHandler.SchemeName,
                            _ => { });
                    }
                });
            });
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();

        // Schedule cleanup on background thread to avoid blocking tests
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(_tempPuzzlePath))
                    Directory.Delete(_tempPuzzlePath, true);
                if (Directory.Exists(_tempLeaderboardPath))
                    Directory.Delete(_tempLeaderboardPath, true);
            }
            catch
            {
                // Ignore cleanup errors - temp directories will be cleaned by OS
            }
        });
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-1"),
                new Claim(ClaimTypes.Name, "Test User")
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
