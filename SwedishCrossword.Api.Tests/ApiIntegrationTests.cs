using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

#pragma warning disable AD0001 // Analyzer threw an exception (TUnit.Analyzers.DisposableFieldPropertyAnalyzer bug)
public class ApiIntegrationTests : IAsyncDisposable
{
    private ApiTestFixture _fixture = null!;

    [Before(Test)]
    public void Setup()
    {
        _fixture = new ApiTestFixture();
    }

    [After(Test)]
    public async Task Cleanup()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Cleanup();
        GC.SuppressFinalize(this);
    }

    private WebApplicationFactory<Program> Factory => _fixture.Factory;
    private HttpClient Client => _fixture.Client;
    private string TempPuzzlePath => _fixture.TempPuzzlePath;
    private string TempLeaderboardPath => _fixture.TempLeaderboardPath;

    // -----------------------------------------------------------------------
    // Health check
    // -----------------------------------------------------------------------

    [Test]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/health");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Stats
    // -----------------------------------------------------------------------

    [Test]
    public async Task StatsEndpoint_ReturnsExpectedShape()
    {
        var response = await Client.GetAsync("/api/stats");

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
        var response = await Client.GetAsync("/api/puzzle/not-a-date");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PuzzleByDate_PastDate_NoFile_ReturnsNotFound()
    {
        var pastDate = GetSwedishDate().AddDays(-10).ToString("yyyy-MM-dd");

        var response = await Client.GetAsync($"/api/puzzle/{pastDate}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PuzzleByDate_PreSeededFile_ReturnsContent()
    {
        var date = GetSwedishDate().ToString("yyyy-MM-dd");
        var puzzleJson = """{"test":true}""";
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{date}.json"), puzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{date}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        // Simple JSON without cells is returned as-is (no token injected)
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    [Test]
    public async Task PuzzleByDate_FutureDate_ReturnsNotFound()
    {
        var futureDate = GetSwedishDate().AddDays(3);
        var puzzleJson = """{"test":true}""";
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{futureDate:yyyy-MM-dd}.json"), puzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{futureDate:yyyy-MM-dd}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PuzzleDates_ExcludesFutureDates()
    {
        var today = GetSwedishDate();
        var pastDate = today.AddDays(-2);
        var futureDate = today.AddDays(3);

        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{pastDate:yyyy-MM-dd}.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today:yyyy-MM-dd}.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{futureDate:yyyy-MM-dd}.json"), "{}");

        var response = await Client.GetAsync("/api/puzzle/dates");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<PuzzleDateEntry[]>();
        await Assert.That(items).IsNotNull();
        var dates = items!.Select(i => i.Date).ToArray();
        await Assert.That(dates).Contains($"{today:yyyy-MM-dd}");
        await Assert.That(dates).Contains($"{pastDate:yyyy-MM-dd}");
        await Assert.That(dates).DoesNotContain($"{futureDate:yyyy-MM-dd}");
    }

    // -----------------------------------------------------------------------
    // Leaderboard
    // -----------------------------------------------------------------------

    [Test]
    public async Task LeaderboardGet_ReturnsEmptyObjectByDefault()
    {
        var response = await Client.GetAsync("/api/leaderboard");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo("{\"scores\":{}}");
    }

    [Test]
    public async Task LegacyJsonFiles_MigratedToSqliteOnStartup()
    {
        // Dispose the default factory — we need to seed files BEFORE the app starts
        await _fixture.DisposeAsync();

        // Create a fresh leaderboard directory and seed legacy JSON files
        var lbPath = Path.Combine(Path.GetTempPath(), "sc-test-lb-migrate-" + Guid.NewGuid());
        Directory.CreateDirectory(lbPath);

        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var leaderboardKey = $"{today}-standard";

        // Seed current.json (scores)
        var currentJson = """{"scores":{"LEADERBOARD_KEY":[{"name":"Anna","time":45.0,"timestamp":1705320000000,"puzzleHash":"abc","hintsUsed":0,"wordHintsUsed":0}]}}""".Replace("LEADERBOARD_KEY", leaderboardKey);
        await File.WriteAllTextAsync(Path.Combine(lbPath, "current.json"), currentJson);

        // Seed history/{date}.json
        var historyDir = Path.Combine(lbPath, "history");
        Directory.CreateDirectory(historyDir);
        var historyJson = """[{"name":"Erik","time":90.5,"timestamp":1705320000000,"puzzleHash":"def","puzzleSize":"standard","hintsUsed":1,"wordHintsUsed":0}]""";
        await File.WriteAllTextAsync(Path.Combine(historyDir, $"{today}.json"), historyJson);

        // Start the app — migration should run automatically
        _fixture = new ApiTestFixture(lbPath);

        // Verify scores migrated
        var lbResponse = await Client.GetAsync("/api/leaderboard");
        var lbContent = await lbResponse.Content.ReadAsStringAsync();
        await Assert.That(lbContent).Contains("Anna");
        await Assert.That(lbContent).Contains("45");

        // Verify history migrated
        var histResponse = await Client.GetAsync("/api/leaderboard/history?days=1");
        var histJson = await histResponse.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(histJson.TryGetProperty(today, out var entries)).IsTrue();
        await Assert.That(entries.GetArrayLength()).IsEqualTo(1);
        await Assert.That(entries[0].GetProperty("name").GetString()).IsEqualTo("Erik");

        // Verify old files were renamed
        await Assert.That(File.Exists(Path.Combine(lbPath, "current.json"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(lbPath, "current.json.migrated"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(lbPath, "history"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(lbPath, "history.migrated"))).IsTrue();

        // Cleanup the extra directory
        await _fixture.DisposeAsync();
        if (Directory.Exists(lbPath)) Directory.Delete(lbPath, true);

        // Re-create default fixture so [After(Test)] cleanup doesn't fail
        _fixture = new ApiTestFixture();
    }

    [Test]
    public async Task LeaderboardHistoryPost_MissingToken_ReturnsForbidden()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            entry = new { name = "Testare", time = 120.5, timestamp = 1705320000000L, puzzleHash = "abc123" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task LeaderboardHistoryPost_ValidToken_ReturnsOk()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        // Fetch puzzle to obtain a valid submission token
        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = puzzle.GetProperty("puzzleHash").GetString()!;

        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
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
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10, "2025-01-15");

        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
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
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var token = tokenService.GenerateToken("abc", 10, today);

        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
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
        var response = await Client.GetAsync("/api/leaderboard/history?days=7");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardHistoryRoundTrip_PostThenGet()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        // Fetch puzzle to obtain a valid submission token
        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = puzzle.GetProperty("puzzleHash").GetString()!;

        await Client.PostAsJsonAsync("/api/leaderboard/history", new
        {
            date = today,
            token,
            entry = new { name = "Anna", time = 95.3, timestamp = 1705320000000L, puzzleHash = hash }
        });

        var response = await Client.GetAsync("/api/leaderboard/history?days=1");
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
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var puzzleJson = """{"today":true}""";
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), puzzleJson);

        var response = await Client.GetAsync("/api/puzzle/today");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo(puzzleJson);
    }

    [Test]
    public async Task PuzzleToday_NoFile_Returns503()
    {
        // No puzzle file seeded — warmup service hasn't run
        var response = await Client.GetAsync("/api/puzzle/today");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
    }

    [Test]
    public async Task PuzzleToday_SmallSize_FallsBackToStandard()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var puzzleJson = """{"fallback":true}""";
        Directory.CreateDirectory(TempPuzzlePath);
        // Only create the standard file, not the small variant
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), puzzleJson);

        var response = await Client.GetAsync("/api/puzzle/today?size=small");

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
        var pastDate = GetSwedishDate().AddDays(-1).ToString("yyyy-MM-dd");
        var puzzleJson = """{"standard":true}""";
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{pastDate}.json"), puzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{pastDate}?size=small");

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
        Directory.CreateDirectory(TempPuzzlePath);

        var response = await Client.GetAsync("/api/puzzle/dates");

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
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        // Fetch puzzle to get submission token
        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = puzzle.GetProperty("puzzleHash").GetString()!;

        var response = await Client.PostAsJsonAsync("/api/scores", new
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
        var response = await Client.PostAsJsonAsync("/api/scores", new
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
        var response = await Client.PostAsJsonAsync("/api/scores", new
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
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = puzzle.GetProperty("puzzleHash").GetString()!;

        // Submit with impossibly fast time (0.1s for 5 cells = below 1.5s minimum)
        var response = await Client.PostAsJsonAsync("/api/scores", new
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
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        var hash = puzzle.GetProperty("puzzleHash").GetString()!;

        await Client.PostAsJsonAsync("/api/scores", new
        {
            token,
            name = "Anna",
            time = 45.0,
            puzzleHash = hash,
            date = today
        });

        // Verify score appears in GET /api/leaderboard
        var lbResponse = await Client.GetAsync("/api/leaderboard");
        var lb = await lbResponse.Content.ReadAsStringAsync();
        await Assert.That(lb).Contains("Anna");
    }

    [Test]
    public async Task ScoresPost_EmptyName_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/scores", new
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
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{today}");
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
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var oldDate = GetSwedishDate().AddDays(-100).ToString("yyyy-MM-dd");
        var token = tokenService.GenerateToken("abc", 10, oldDate);

        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
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
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var token = tokenService.GenerateToken("abc", 10, today);

        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
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
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var token = tokenService.GenerateToken("abc", 10, today);

        var response = await Client.PostAsJsonAsync("/api/leaderboard/history", new
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
        var response = await Client.GetAsync("/api/leaderboard/history");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task LeaderboardHistoryGet_ClampsDaysToMax90()
    {
        var response = await Client.GetAsync("/api/leaderboard/history?days=200");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // Stats endpoint — shape validation
    // -----------------------------------------------------------------------

    [Test]
    public async Task StatsEndpoint_ReturnsJsonContentType()
    {
        var response = await Client.GetAsync("/api/stats");

        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
    }

    // -----------------------------------------------------------------------
    // Puzzle content type
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleByDate_ReturnsJsonContentType()
    {
        var date = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{date}.json"), "{}");

        var response = await Client.GetAsync($"/api/puzzle/{date}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
    }

    // -----------------------------------------------------------------------
    // Answer stripping — puzzle served to client must not contain answers
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleWithCells_StripsLettersFromCells()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cells = puzzle.GetProperty("cells");

        // Every non-null cell should NOT have a "letter" property
        for (int row = 0; row < cells.GetArrayLength(); row++)
        {
            var rowArray = cells[row];
            for (int col = 0; col < rowArray.GetArrayLength(); col++)
            {
                var cell = rowArray[col];
                if (cell.ValueKind == JsonValueKind.Object)
                    await Assert.That(cell.TryGetProperty("letter", out _)).IsFalse();
            }
        }
    }

    [Test]
    public async Task PuzzleWithCells_StripsAnswersFromClues()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await response.Content.ReadFromJsonAsync<JsonElement>();
        var clues = puzzle.GetProperty("clues");

        foreach (var direction in new[] { "across", "down" })
        {
            foreach (var clue in clues.GetProperty(direction).EnumerateArray())
            {
                await Assert.That(clue.TryGetProperty("answer", out _)).IsFalse();
                // clue text and number should still be present
                await Assert.That(clue.TryGetProperty("clue", out _)).IsTrue();
                await Assert.That(clue.TryGetProperty("number", out _)).IsTrue();
            }
        }
    }

    [Test]
    public async Task PuzzleWithCells_StillIncludesPuzzleHashAndDate()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var response = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await response.Content.ReadFromJsonAsync<JsonElement>();

        await Assert.That(puzzle.TryGetProperty("puzzleHash", out _)).IsTrue();
        await Assert.That(puzzle.TryGetProperty("puzzleDate", out var dateEl)).IsTrue();
        await Assert.That(dateEl.GetString()).IsEqualTo(today);
    }

    // -----------------------------------------------------------------------
    // POST /api/puzzle/check — server-side answer validation
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleCheck_AllCorrect_ReturnsSolved()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;

        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token,
            puzzleDate = today,
            cells = new Dictionary<string, string>
            {
                ["0,0"] = "K",
                ["0,1"] = "A",
                ["0,2"] = "T",
                ["2,0"] = "E",
                ["2,1"] = "N"
            }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(data.GetProperty("solved").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task PuzzleCheck_PartiallyCorrect_ReturnsNotSolved()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;

        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token,
            puzzleDate = today,
            cells = new Dictionary<string, string>
            {
                ["0,0"] = "K",
                ["0,1"] = "A",
                ["0,2"] = "X",  // wrong
                ["2,0"] = "E",
                ["2,1"] = "N"
            }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(data.GetProperty("solved").GetBoolean()).IsFalse();

        var results = data.GetProperty("results");
        await Assert.That(results.GetProperty("0,0").GetBoolean()).IsTrue();
        await Assert.That(results.GetProperty("0,2").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task PuzzleCheck_EmptyCells_ReturnsNotSolved()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;

        // Submit with no cells filled
        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token,
            puzzleDate = today,
            cells = new Dictionary<string, string>()
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(data.GetProperty("solved").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task PuzzleCheck_CaseInsensitive_Matches()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;

        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token,
            puzzleDate = today,
            cells = new Dictionary<string, string>
            {
                ["0,0"] = "k",
                ["0,1"] = "a",
                ["0,2"] = "t",  // lowercase
                ["2,0"] = "e",
                ["2,1"] = "n"
            }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(data.GetProperty("solved").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task PuzzleCheck_MissingToken_Returns403()
    {
        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token = "",
            puzzleDate = "2025-01-15",
            cells = new Dictionary<string, string> { ["0,0"] = "K" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PuzzleCheck_TamperedToken_Returns403()
    {
        var fakeToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake:0:0:bad"));

        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token = fakeToken,
            puzzleDate = "2025-01-15",
            cells = new Dictionary<string, string> { ["0,0"] = "K" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PuzzleCheck_InvalidDate_ReturnsBadRequest()
    {
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10, "2025-01-15");

        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token,
            puzzleDate = "not-a-date",
            cells = new Dictionary<string, string> { ["0,0"] = "K" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PuzzleCheck_MissingPuzzleFile_ReturnsNotFound()
    {
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10, "2020-01-01");
        Directory.CreateDirectory(TempPuzzlePath);

        var response = await Client.PostAsJsonAsync("/api/puzzle/check", new
        {
            token,
            puzzleDate = "2020-01-01",
            cells = new Dictionary<string, string> { ["0,0"] = "K" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // POST /api/puzzle/hint — server-side letter hints
    // -----------------------------------------------------------------------

    [Test]
    public async Task PuzzleHint_SingleCell_ReturnsLetter()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;

        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token,
            puzzleDate = today,
            cells = new[] { new[] { 0, 0 } }  // row 0, col 0 = "K"
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        var letters = data.GetProperty("letters");
        await Assert.That(letters.GetProperty("0,0").GetString()).IsEqualTo("K");
    }

    [Test]
    public async Task PuzzleHint_MultipleCells_ReturnsAllLetters()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;
        int[][] cells = [[0, 0], [0, 1], [0, 2]];
        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token,
            puzzleDate = today,
            cells
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        var letters = data.GetProperty("letters");
        await Assert.That(letters.GetProperty("0,0").GetString()).IsEqualTo("K");
        await Assert.That(letters.GetProperty("0,1").GetString()).IsEqualTo("A");
        await Assert.That(letters.GetProperty("0,2").GetString()).IsEqualTo("T");
    }

    [Test]
    public async Task PuzzleHint_BlockedCell_SkipsIt()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        Directory.CreateDirectory(TempPuzzlePath);
        await File.WriteAllTextAsync(Path.Combine(TempPuzzlePath, $"puzzle-{today}.json"), TestPuzzleJson);

        var puzzleResponse = await Client.GetAsync($"/api/puzzle/{today}");
        var puzzle = await puzzleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = puzzle.GetProperty("submissionToken").GetString()!;

        // Cell 1,0 is null (blocked) in the test puzzle
        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token,
            puzzleDate = today,
            cells = new[] { new[] { 1, 0 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        var letters = data.GetProperty("letters");
        // Should not contain the blocked cell
        await Assert.That(letters.TryGetProperty("1,0", out _)).IsFalse();
    }

    [Test]
    public async Task PuzzleHint_MissingToken_Returns403()
    {
        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token = "",
            puzzleDate = "2025-01-15",
            cells = new[] { new[] { 0, 0 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PuzzleHint_TamperedToken_Returns403()
    {
        var fakeToken = Convert.ToBase64String(Encoding.UTF8.GetBytes("fake:0:0:bad"));

        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token = fakeToken,
            puzzleDate = "2025-01-15",
            cells = new[] { new[] { 0, 0 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PuzzleHint_InvalidDate_ReturnsBadRequest()
    {
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10, "2025-01-15");

        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token,
            puzzleDate = "bad-date",
            cells = new[] { new[] { 0, 0 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PuzzleHint_EmptyCells_ReturnsBadRequest()
    {
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10, "2025-01-15");

        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token,
            puzzleDate = "2025-01-15",
            cells = Array.Empty<int[]>()
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task PuzzleHint_MissingPuzzleFile_ReturnsNotFound()
    {
        var tokenService = _fixture.Factory.Services.GetRequiredService<SubmissionTokenService>();
        var token = tokenService.GenerateToken("abc", 10, "2020-01-01");
        Directory.CreateDirectory(TempPuzzlePath);

        var response = await Client.PostAsJsonAsync("/api/puzzle/hint", new
        {
            token,
            puzzleDate = "2020-01-01",
            cells = new[] { new[] { 0, 0 } }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // Analytics endpoints
    // -----------------------------------------------------------------------

    [Test]
    public async Task AnalyticsSummary_ReturnsOk_WithExpectedShape()
    {
        var response = await Client.GetAsync("/api/analytics/summary");

        // Analytics endpoints require authentication
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task AnalyticsDaily_DefaultDays_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/analytics/daily");

        // Analytics endpoints require authentication
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task AnalyticsDaily_WithDaysParameter_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/analytics/daily?days=7");

        // Analytics endpoints require authentication
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task AnalyticsPlayers_DefaultLimit_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/analytics/players");

        // Analytics endpoints require authentication
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
    }

    [Test]
    public async Task AnalyticsPlayers_WithLimitParameter_ReturnsOk()
    {
        var response = await Client.GetAsync("/api/analytics/players?limit=5");

        // Analytics endpoints require authentication
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
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

    private static DateOnly GetSwedishDate()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }
}

file record PuzzleDateEntry(string Date, string[] Sizes);
