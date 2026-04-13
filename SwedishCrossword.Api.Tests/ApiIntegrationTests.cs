using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
        // Simple JSON without cells is returned as-is (no token injected)
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    [Test]
    public async Task PuzzleByDate_FutureDate_ReturnsNotFound()
    {
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        var puzzleJson = """{"test":true}""";
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{futureDate:yyyy-MM-dd}.json"), puzzleJson);

        var response = await _client.GetAsync($"/api/puzzle/{futureDate:yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PuzzleDates_ExcludesFutureDates()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pastDate = today.AddDays(-2);
        var futureDate = today.AddDays(3);

        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{pastDate:yyyy-MM-dd}.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today:yyyy-MM-dd}.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{futureDate:yyyy-MM-dd}.json"), "{}");

        var response = await _client.GetAsync("/api/puzzle/dates");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var dates = await response.Content.ReadFromJsonAsync<string[]>();
        await Assert.That(dates).IsNotNull();
        await Assert.That(dates!).Contains($"{today:yyyy-MM-dd}");
        await Assert.That(dates!).Contains($"{pastDate:yyyy-MM-dd}");
        await Assert.That(dates!).DoesNotContain($"{futureDate:yyyy-MM-dd}");
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
    public async Task LeaderboardHistoryPost_MissingToken_ReturnsForbidden()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            entry = new { name = "Testare", time = 120.5, timestamp = 1705320000000L, puzzleHash = "abc123" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task LeaderboardHistoryPost_ValidToken_ReturnsOk()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        // Fetch puzzle to obtain a valid submission token
        var puzzleResponse = await _client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = ComputePuzzleHash(puzzle);

        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            token,
            entry = new { name = "Testare", time = 120.5, timestamp = 1705320000000L, puzzleHash = hash }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardHistoryPost_InvalidDate_ReturnsBadRequest()
    {
        var tokenService = _factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10);

        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = "bad-date",
            token,
            entry = new { name = "Test", time = 42.0, timestamp = (string?)null, puzzleHash = "abc" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LeaderboardHistoryPost_MissingEntry_ReturnsBadRequest()
    {
        var tokenService = _factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10);
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            token,
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        // Fetch puzzle to obtain a valid submission token
        var puzzleResponse = await _client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = ComputePuzzleHash(puzzle);

        await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            token,
            entry = new { name = "Anna", time = 95.3, timestamp = 1705320000000L, puzzleHash = hash }
        });

        var response = await _client.GetAsync("/api/leaderboard/history?days=1");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        await Assert.That(json.TryGetProperty(today, out var entries)).IsTrue();
        await Assert.That(entries.GetArrayLength()).IsEqualTo(1);
    }

    // -----------------------------------------------------------------------
    // Puzzle today
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleToday_PreSeededFile_ReturnsContent()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var puzzleJson = """{"today":true}""";
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), puzzleJson);

        var response = await _client.GetAsync("/api/puzzle/today");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    [Test]
    public async Task PuzzleToday_NoFile_Returns503()
    {
        // No puzzle file seeded — warmup service hasn't run
        var response = await _client.GetAsync("/api/puzzle/today");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task PuzzleToday_SmallSize_FallsBackToStandard()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var puzzleJson = """{"fallback":true}""";
        Directory.CreateDirectory(_tempPuzzlePath);
        // Only create the standard file, not the small variant
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), puzzleJson);

        var response = await _client.GetAsync("/api/puzzle/today?size=small");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    // -----------------------------------------------------------------------
    // Puzzle by date — size parameter
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleByDate_SmallSize_FallsBackToStandard()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");
        var puzzleJson = """{"standard":true}""";
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{pastDate}.json"), puzzleJson);

        var response = await _client.GetAsync($"/api/puzzle/{pastDate}?size=small");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    // -----------------------------------------------------------------------
    // Puzzle dates — empty directory
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleDates_EmptyDirectory_ReturnsEmptyArray()
    {
        Directory.CreateDirectory(_tempPuzzlePath);

        var response = await _client.GetAsync("/api/puzzle/dates");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var dates = await response.Content.ReadFromJsonAsync<string[]>();
        await Assert.That(dates).IsNotNull();
        await Assert.That(dates!.Length).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // Scores (token-validated submission)
    // -----------------------------------------------------------------------

    [Test]
    public async Task ScoresPost_ValidToken_ReturnsOk()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        // Fetch puzzle to get submission token
        var puzzleResponse = await _client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = ComputePuzzleHash(puzzle);

        var response = await _client.PostAsJsonAsync("/api/scores", new
        {
            token,
            name = "Testare",
            time = 60.0,
            puzzleHash = hash,
            date = today
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(result.GetProperty("success").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task ScoresPost_MissingToken_Returns403()
    {
        var response = await _client.PostAsJsonAsync("/api/scores", new
        {
            token = "",
            name = "Testare",
            time = 60.0,
            puzzleHash = "abc",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ScoresPost_TamperedToken_Returns403()
    {
        var fakeToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake:0:0:bad"));
        var response = await _client.PostAsJsonAsync("/api/scores", new
        {
            token = fakeToken,
            name = "Testare",
            time = 60.0,
            puzzleHash = "fake",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ScoresPost_TooFast_Returns403()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await _client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = ComputePuzzleHash(puzzle);

        // Submit with impossibly fast time (0.1s for 5 cells = below 1.5s minimum)
        var response = await _client.PostAsJsonAsync("/api/scores", new
        {
            token,
            name = "Cheater",
            time = 0.1,
            puzzleHash = hash,
            date = today
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ScoresPost_AppearsInLeaderboard()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await _client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = ComputePuzzleHash(puzzle);

        await _client.PostAsJsonAsync("/api/scores", new
        {
            token,
            name = "Anna",
            time = 45.0,
            puzzleHash = hash,
            date = today
        });

        // Verify score appears in GET /api/leaderboard
        var lbResponse = await _client.GetAsync("/api/leaderboard");
        var lb = await lbResponse.Content.ReadAsStringAsync();
        await Assert.That(lb).Contains("Anna");
    }

    [Test]
    public async Task ScoresPost_EmptyName_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/scores", new
        {
            token = "x",
            name = "   ",
            time = 60.0,
            puzzleHash = "abc",
            date = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PuzzleWithCells_IncludesSubmissionToken()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var response = await _client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await response.Content.ReadFromJsonAsync<JsonElement>();

        await Assert.That(puzzle.TryGetProperty("submissionToken", out _)).IsTrue();
        await Assert.That(puzzle.TryGetProperty("cellCount", out var cc)).IsTrue();
        await Assert.That(cc.GetInt32()).IsEqualTo(5);
    }

    // -----------------------------------------------------------------------
    // Leaderboard history — edge cases
    // -----------------------------------------------------------------------

    [Test]
    public async Task LeaderboardHistoryPost_DateTooOld_ReturnsBadRequest()
    {
        var tokenService = _factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10);
        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-100).ToString("yyyy-MM-dd");

        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = oldDate,
            token,
            entry = new { name = "Test", time = 42.0, timestamp = 1705320000000L, puzzleHash = "abc" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LeaderboardHistoryPost_NegativeTime_ReturnsBadRequest()
    {
        var tokenService = _factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10);
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            token,
            entry = new { name = "Test", time = -1.0, timestamp = 1705320000000L, puzzleHash = "abc" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LeaderboardHistoryPost_EmptyName_ReturnsBadRequest()
    {
        var tokenService = _factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10);
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

        var response = await _client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            token,
            entry = new { name = "   ", time = 42.0, timestamp = 1705320000000L, puzzleHash = "abc" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task LeaderboardHistoryGet_DefaultDays_ReturnsOk()
    {
        // No days parameter — should default to 30
        var response = await _client.GetAsync("/api/leaderboard/history");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardHistoryGet_ClampsDaysToMax90()
    {
        var response = await _client.GetAsync("/api/leaderboard/history?days=200");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Stats endpoint — shape validation
    // -----------------------------------------------------------------------

    [Test]
    public async Task StatsEndpoint_ReturnsJsonContentType()
    {
        var response = await _client.GetAsync("/api/stats");

        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
    }

    // -----------------------------------------------------------------------
    // Puzzle content type
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleByDate_ReturnsJsonContentType()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Directory.CreateDirectory(_tempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(_tempPuzzlePath, $"puzzle-{date}.json"), "{}");

        var response = await _client.GetAsync($"/api/puzzle/{date}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private const string TestPuzzleJson = """
        {
            "width": 3,
            "height": 3,
            "createdAt": "2025-01-15 12:00",
            "wordCount": 2,
            "fillPercentage": 66.7,
            "cells": [
                [{"letter":"K"},{"letter":"A"},{"letter":"T"}],
                [null,null,null],
                [{"letter":"E"},{"letter":"N"},null]
            ],
            "clues": {
                "across": [{"number":1,"clue":"Djur","answer":"KAT","cells":[[0,0],[0,1],[0,2]]}],
                "down": [{"number":2,"clue":"En","answer":"EN","cells":[[2,0],[2,1]]}]
            }
        }
        """;

    /// <summary>
    /// Replicates the client-side <c>generatePuzzleHash()</c> algorithm.
    /// </summary>
    private static string ComputePuzzleHash(JsonElement puzzle)
    {
        var cells = puzzle.GetProperty("cells");
        var height = puzzle.GetProperty("height").GetInt32();
        var width = puzzle.GetProperty("width").GetInt32();
        var sb = new StringBuilder();
        for (int row = 0; row < height; row++)
        {
            var rowArray = cells[row];
            for (int col = 0; col < width; col++)
            {
                var cell = rowArray[col];
                sb.Append(cell.ValueKind == JsonValueKind.Null ? '#' : cell.GetProperty("letter").GetString());
            }
        }
        return ToBase36(JavaStringHash(sb.ToString()));
    }

    private static int JavaStringHash(string s)
    {
        unchecked
        {
            int hash = 0;
            foreach (char c in s)
                hash = ((hash << 5) - hash) + c;
            return hash;
        }
    }

    private static string ToBase36(int value)
    {
        if (value == 0) return "0";
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        bool negative = value < 0;
        long v = negative ? -(long)value : value;
        var result = new char[14];
        int pos = result.Length;
        while (v > 0)
        {
            result[--pos] = digits[(int)(v % 36)];
            v /= 36;
        }
        if (negative) result[--pos] = '-';
        return new string(result, pos, result.Length - pos);
    }
}
