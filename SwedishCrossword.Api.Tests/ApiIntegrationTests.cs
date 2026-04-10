using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

public class ApiIntegrationTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _tempPuzzlePath = null!;
    private string _tempLeaderboardPath = null!;

    [Before(Test)]
    public void Setup()
    {
        _tempPuzzlePath = Path.Combine(Path.GetTempPath(), "sc-test-puzzles-" + Guid.NewGuid());
        _tempLeaderboardPath = Path.Combine(Path.GetTempPath(), "sc-test-lb-" + Guid.NewGuid());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Storage:PuzzlePath", _tempPuzzlePath);
                builder.UseSetting("Storage:LeaderboardPath", _tempLeaderboardPath);
            });

        _client = _factory.CreateClient();
    }

    [After(Test)]
    public void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();

        if (Directory.Exists(_tempPuzzlePath))
            Directory.Delete(_tempPuzzlePath, true);
        if (Directory.Exists(_tempLeaderboardPath))
            Directory.Delete(_tempLeaderboardPath, true);
    }

    // -----------------------------------------------------------------------
    // Health check
    // -----------------------------------------------------------------------

    [Test]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Stats
    // -----------------------------------------------------------------------

    [Test]
    public async Task StatsEndpoint_ReturnsExpectedShape()
    {
        var response = await _client.GetAsync("/api/stats");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(json.TryGetProperty("wordCount", out _)).IsTrue();
        await Assert.That(json.TryGetProperty("availableDifficulties", out _)).IsTrue();
    }

    // -----------------------------------------------------------------------
    // Puzzle endpoints
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleByDate_InvalidDate_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/puzzle/not-a-date");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PuzzleByDate_PastDate_NoFile_ReturnsNotFound()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10).ToString("yyyy-MM-dd");

        var response = await _client.GetAsync($"/api/puzzle/{pastDate}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PuzzleByDate_PreSeededFile_ReturnsContent()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var puzzleJson = """{"test":true}""";
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{date}.json"), puzzleJson);

        var response = await _client.GetAsync($"/api/puzzle/{date}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    // -----------------------------------------------------------------------
    // Leaderboard
    // -----------------------------------------------------------------------

    [Test]
    public async Task LeaderboardGet_ReturnsEmptyObjectByDefault()
    {
        var response = await _client.GetAsync("/api/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo("{}");
    }

    [Test]
    public async Task LeaderboardHistoryPost_ValidEntry_ReturnsOk()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            entry = new { name = "Testare", time = 120.5, timestamp = 1705320000000L, puzzleHash = "abc123" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardHistoryPost_InvalidDate_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = "bad-date",
            entry = new { name = "Test", time = 42.0, timestamp = (string?)null, puzzleHash = (string?)null }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LeaderboardHistoryPost_MissingEntry_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = "2025-01-15",
            entry = (object?)null
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LeaderboardHistoryGet_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/leaderboard/history?days=7");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardHistoryRoundTrip_PostThenGet()
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            entry = new { name = "Anna", time = 95.3, timestamp = 1705320000000L, puzzleHash = "hash1" }
        });

        var response = await _client.GetAsync("/api/leaderboard/history?days=1");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        await Assert.That(json.TryGetProperty(today, out var entries)).IsTrue();
        await Assert.That(entries.GetArrayLength()).IsEqualTo(1);
    }
}
