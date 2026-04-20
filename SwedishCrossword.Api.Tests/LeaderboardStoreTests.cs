using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

public class LeaderboardStoreTests
{
    private string _tempDir = null!;
    private LeaderboardStore _store = null!;

    [Before(Test)]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sc-test-store-" + Guid.NewGuid());
        _store = CreateStore(_tempDir);
    }

    [After(Test)]
    public void Cleanup()
    {
        _store?.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // -----------------------------------------------------------------------
    // SanitiseName
    // -----------------------------------------------------------------------

    [Test]
    public async Task SanitiseName_Null_ReturnsEmpty()
    {
        await Assert.That(LeaderboardStore.SanitiseName(null)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitiseName_Whitespace_ReturnsEmpty()
    {
        await Assert.That(LeaderboardStore.SanitiseName("   ")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitiseName_StripsControlCharacters()
    {
        await Assert.That(LeaderboardStore.SanitiseName("Anna\t\nB")).IsEqualTo("AnnaB");
    }

    [Test]
    public async Task SanitiseName_TrimsWhitespace()
    {
        await Assert.That(LeaderboardStore.SanitiseName("  Erik  ")).IsEqualTo("Erik");
    }

    [Test]
    public async Task SanitiseName_TruncatesAt30Characters()
    {
        var longName = new string('A', 50);
        var result = LeaderboardStore.SanitiseName(longName);
        await Assert.That(result.Length).IsEqualTo(30);
    }

    [Test]
    public async Task SanitiseName_PreservesSwedishCharacters()
    {
        await Assert.That(LeaderboardStore.SanitiseName("Åsa Öberg")).IsEqualTo("Åsa Öberg");
    }

    // -----------------------------------------------------------------------
    // GetCurrentAsync — empty database
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetCurrentAsync_EmptyDatabase_ReturnsEmptyScoresEnvelope()
    {
        var json = await _store.GetCurrentAsync();

        await Assert.That(json).IsEqualTo("""{"scores":{}}""");
    }

    // -----------------------------------------------------------------------
    // AppendScoreAsync — basic insertion
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendScoreAsync_SingleEntry_AppearsInResult()
    {
        var entry = new ScoreRecord("Anna", 45.0, 1000L, "hash1");

        var result = await _store.AppendScoreAsync("2026-05-20-standard", entry);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Name).IsEqualTo("Anna");
        await Assert.That(result[0].Time).IsEqualTo(45.0);
    }

    [Test]
    public async Task AppendScoreAsync_MultipleEntries_SortedByTime()
    {
        const string key = "2026-05-20-standard";

        await _store.AppendScoreAsync(key, new ScoreRecord("Slow", 90.0, 1000L, "h"));
        await _store.AppendScoreAsync(key, new ScoreRecord("Fast", 30.0, 2000L, "h"));
        var result = await _store.AppendScoreAsync(key, new ScoreRecord("Mid", 60.0, 3000L, "h"));

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].Name).IsEqualTo("Fast");
        await Assert.That(result[1].Name).IsEqualTo("Mid");
        await Assert.That(result[2].Name).IsEqualTo("Slow");
    }

    [Test]
    public async Task AppendScoreAsync_AppearsInGetCurrentAsync()
    {
        await _store.AppendScoreAsync("2026-05-20-standard", new ScoreRecord("Anna", 45.0, 1000L, "h"));

        var json = await _store.GetCurrentAsync();

        await Assert.That(json).Contains("Anna");
        await Assert.That(json).Contains("45");
        await Assert.That(json).Contains("2026-05-20-standard");
    }

    // -----------------------------------------------------------------------
    // AppendScoreAsync — deduplication
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendScoreAsync_DuplicateEntry_NotInserted()
    {
        const string key = "2026-05-20-standard";
        var entry = new ScoreRecord("Anna", 45.0, 1000L, "hash1");

        await _store.AppendScoreAsync(key, entry);
        var result = await _store.AppendScoreAsync(key, entry);

        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AppendScoreAsync_SameNameDifferentTime_BothInserted()
    {
        const string key = "2026-05-20-standard";

        await _store.AppendScoreAsync(key, new ScoreRecord("Anna", 45.0, 1000L, "h"));
        var result = await _store.AppendScoreAsync(key, new ScoreRecord("Anna", 50.0, 2000L, "h"));

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AppendScoreAsync_NearDuplicateWithinTolerance_NotInserted()
    {
        const string key = "2026-05-20-standard";

        await _store.AppendScoreAsync(key, new ScoreRecord("Anna", 45.0, 1000L, "h"));
        // Time difference < 0.001 and same timestamp → duplicate
        var result = await _store.AppendScoreAsync(key, new ScoreRecord("Anna", 45.0005, 1000L, "h"));

        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AppendScoreAsync_NullTimestamp_DeduplicatesCorrectly()
    {
        const string key = "2026-05-20-standard";
        var entry = new ScoreRecord("Anna", 45.0, null, "h");

        await _store.AppendScoreAsync(key, entry);
        var result = await _store.AppendScoreAsync(key, entry);

        await Assert.That(result.Count).IsEqualTo(1);
    }

    // -----------------------------------------------------------------------
    // AppendScoreAsync — top-10 trimming
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendScoreAsync_KeepsOnlyTop10PerKey()
    {
        const string key = "2026-05-20-standard";

        for (int i = 1; i <= 12; i++)
            await _store.AppendScoreAsync(key, new ScoreRecord($"P{i}", i * 10.0, i * 1000L, "h"));

        var result = await _store.AppendScoreAsync(key, new ScoreRecord("P13", 5.0, 13000L, "h"));

        // P13 (5.0) should be #1, original P1–P9 (10–90) should fill the rest, P10+ (100+) trimmed
        await Assert.That(result.Count).IsEqualTo(10);
        await Assert.That(result[0].Name).IsEqualTo("P13");
        await Assert.That(result[0].Time).IsEqualTo(5.0);
    }

    [Test]
    public async Task AppendScoreAsync_SlowScoreTrimmedImmediately()
    {
        const string key = "2026-05-20-standard";

        // Fill with 10 fast scores
        for (int i = 1; i <= 10; i++)
            await _store.AppendScoreAsync(key, new ScoreRecord($"P{i}", i * 1.0, i * 1000L, "h"));

        // Add a slow score that should be trimmed
        var result = await _store.AppendScoreAsync(key, new ScoreRecord("Slow", 999.0, 99000L, "h"));

        await Assert.That(result.Count).IsEqualTo(10);
        await Assert.That(result.Any(r => r.Name == "Slow")).IsFalse();
    }

    // -----------------------------------------------------------------------
    // AppendScoreAsync — key isolation
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendScoreAsync_DifferentKeys_Independent()
    {
        await _store.AppendScoreAsync("2026-05-20-standard", new ScoreRecord("Anna", 45.0, 1000L, "h"));
        await _store.AppendScoreAsync("2026-05-20-small", new ScoreRecord("Erik", 30.0, 2000L, "h"));

        var json = await _store.GetCurrentAsync();
        using var doc = JsonDocument.Parse(json);
        var scores = doc.RootElement.GetProperty("scores");

        await Assert.That(scores.GetProperty("2026-05-20-standard").GetArrayLength()).IsEqualTo(1);
        await Assert.That(scores.GetProperty("2026-05-20-small").GetArrayLength()).IsEqualTo(1);
    }

    // -----------------------------------------------------------------------
    // AppendScoreAsync — pruning old keys
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendScoreAsync_PrunesKeysOlderThan7Days()
    {
        var oldDate = GetSwedishDate().AddDays(-10).ToString("yyyy-MM-dd");
        var todayDate = GetSwedishDate().ToString("yyyy-MM-dd");

        await _store.AppendScoreAsync($"{oldDate}-standard", new ScoreRecord("Old", 45.0, 1000L, "h"));
        await _store.AppendScoreAsync($"{todayDate}-standard", new ScoreRecord("New", 50.0, 2000L, "h"));

        // Pruning is now performed by the background service
        await _store.PruneOldEntriesAsync();

        var json = await _store.GetCurrentAsync();

        await Assert.That(json).DoesNotContain(oldDate);
        await Assert.That(json).Contains(todayDate);
    }

    // -----------------------------------------------------------------------
    // AppendScoreAsync — optional fields
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendScoreAsync_PreservesHintCounts()
    {
        var entry = new ScoreRecord("Anna", 45.0, 1000L, "h", HintsUsed: 3, WordHintsUsed: 1);

        var result = await _store.AppendScoreAsync("2026-05-20-standard", entry);

        await Assert.That(result[0].HintsUsed).IsEqualTo(3);
        await Assert.That(result[0].WordHintsUsed).IsEqualTo(1);
    }

    [Test]
    public async Task AppendScoreAsync_NullPuzzleHash_Handled()
    {
        var entry = new ScoreRecord("Anna", 45.0, 1000L, null);

        var result = await _store.AppendScoreAsync("2026-05-20-standard", entry);

        await Assert.That(result[0].PuzzleHash).IsNull();
    }

    // -----------------------------------------------------------------------
    // AppendHistoryAsync — basic insertion
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendHistoryAsync_SingleRecord_AppearsInGetHistory()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var record = new HistoryRecord("Anna", 90.5, 1000L, "hash1", "standard");

        await _store.AppendHistoryAsync(today, record);
        var result = await _store.GetHistoryAsync(1);

        await Assert.That(result.ContainsKey(today)).IsTrue();
        await Assert.That(result[today].Count).IsEqualTo(1);
        await Assert.That(result[today][0].Name).IsEqualTo("Anna");
    }

    // -----------------------------------------------------------------------
    // AppendHistoryAsync — deduplication
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendHistoryAsync_DuplicateRecord_NotInserted()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var record = new HistoryRecord("Anna", 90.5, 1000L, "hash1", "standard");

        await _store.AppendHistoryAsync(today, record);
        await _store.AppendHistoryAsync(today, record);

        var result = await _store.GetHistoryAsync(1);
        await Assert.That(result[today].Count).IsEqualTo(1);
    }

    [Test]
    public async Task AppendHistoryAsync_NullTimestamp_DeduplicatesCorrectly()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var record = new HistoryRecord("Anna", 90.5, null, "hash1", "standard");

        await _store.AppendHistoryAsync(today, record);
        await _store.AppendHistoryAsync(today, record);

        var result = await _store.GetHistoryAsync(1);
        await Assert.That(result[today].Count).IsEqualTo(1);
    }

    // -----------------------------------------------------------------------
    // AppendHistoryAsync — per-hash top-10 trimming
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendHistoryAsync_KeepsTop10PerPuzzleHash()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        for (int i = 1; i <= 12; i++)
            await _store.AppendHistoryAsync(today, new HistoryRecord($"P{i}", i * 10.0, i * 1000L, "sameHash", "standard"));

        var result = await _store.GetHistoryAsync(1);
        await Assert.That(result[today].Count).IsEqualTo(10);
        // Fastest 10 should remain (times 10–100), slowest 2 (110, 120) trimmed
        await Assert.That(result[today][0].Time).IsEqualTo(10.0);
    }

    [Test]
    public async Task AppendHistoryAsync_DifferentHashes_IndependentTop10()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        // 10 for hash A
        for (int i = 1; i <= 10; i++)
            await _store.AppendHistoryAsync(today, new HistoryRecord($"A{i}", i * 1.0, i * 1000L, "hashA", "standard"));

        // 10 for hash B
        for (int i = 1; i <= 10; i++)
            await _store.AppendHistoryAsync(today, new HistoryRecord($"B{i}", i * 1.0, (i + 100) * 1000L, "hashB", "standard"));

        var result = await _store.GetHistoryAsync(1);
        await Assert.That(result[today].Count).IsEqualTo(20);
    }

    // -----------------------------------------------------------------------
    // AppendHistoryAsync — 50-per-date cap
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendHistoryAsync_CapsAt50PerDate()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        // Use different hashes to avoid per-hash trimming (10 per hash × 6 hashes = 60 > 50)
        for (int h = 0; h < 6; h++)
            for (int i = 1; i <= 10; i++)
                await _store.AppendHistoryAsync(today,
                    new HistoryRecord($"P{h}-{i}", (h * 10) + i, ((h * 10) + i) * 1000L, $"hash{h}", "standard"));

        var result = await _store.GetHistoryAsync(1);
        await Assert.That(result[today].Count).IsEqualTo(50);
    }

    // -----------------------------------------------------------------------
    // AppendHistoryAsync — optional fields
    // -----------------------------------------------------------------------

    [Test]
    public async Task AppendHistoryAsync_PreservesAllFields()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var record = new HistoryRecord("Anna", 90.5, 1000L, "hash1", "small", HintsUsed: 2, WordHintsUsed: 1);

        await _store.AppendHistoryAsync(today, record);
        var result = await _store.GetHistoryAsync(1);
        var r = result[today][0];

        await Assert.That(r.Name).IsEqualTo("Anna");
        await Assert.That(r.Time).IsEqualTo(90.5);
        await Assert.That(r.Timestamp).IsEqualTo(1000L);
        await Assert.That(r.PuzzleHash).IsEqualTo("hash1");
        await Assert.That(r.PuzzleSize).IsEqualTo("small");
        await Assert.That(r.HintsUsed).IsEqualTo(2);
        await Assert.That(r.WordHintsUsed).IsEqualTo(1);
    }

    [Test]
    public async Task AppendHistoryAsync_NullOptionalFields_Handled()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var record = new HistoryRecord("Anna", 90.5, null, null, null);

        await _store.AppendHistoryAsync(today, record);
        var result = await _store.GetHistoryAsync(1);
        var r = result[today][0];

        await Assert.That(r.Timestamp).IsNull();
        await Assert.That(r.PuzzleHash).IsNull();
        await Assert.That(r.PuzzleSize).IsNull();
    }

    // -----------------------------------------------------------------------
    // GetHistoryAsync — date range
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetHistoryAsync_OnlyReturnsDaysInRange()
    {
        var today = GetSwedishDate();
        var todayStr = today.ToString("yyyy-MM-dd");
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(todayStr, new HistoryRecord("A", 10.0, 1000L, "h", "s"));
        await _store.AppendHistoryAsync(yesterday, new HistoryRecord("B", 20.0, 2000L, "h", "s"));

        var result = await _store.GetHistoryAsync(2); // today and yesterday

        await Assert.That(result.ContainsKey(todayStr)).IsTrue();
        await Assert.That(result.ContainsKey(yesterday)).IsTrue();
    }

    [Test]
    public async Task GetHistoryAsync_ExcludesDatesOutsideRange()
    {
        var today = GetSwedishDate();
        var fiveDaysAgo = today.AddDays(-5).ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(fiveDaysAgo, new HistoryRecord("Old", 10.0, 1000L, "h", "s"));

        var result = await _store.GetHistoryAsync(3); // only today, yesterday, 2 days ago

        await Assert.That(result.ContainsKey(fiveDaysAgo)).IsFalse();
    }

    [Test]
    public async Task GetHistoryAsync_EmptyDatabase_ReturnsEmptyDictionary()
    {
        var result = await _store.GetHistoryAsync(30);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetHistoryAsync_ResultsSortedByTimePerDate()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(today, new HistoryRecord("Slow", 90.0, 1000L, "h", "s"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("Fast", 30.0, 2000L, "h", "s"));

        var result = await _store.GetHistoryAsync(1);

        await Assert.That(result[today][0].Name).IsEqualTo("Fast");
        await Assert.That(result[today][1].Name).IsEqualTo("Slow");
    }

    // -----------------------------------------------------------------------
    // GetCurrentAsync — JSON format
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetCurrentAsync_JsonFormat_HasScoresEnvelope()
    {
        await _store.AppendScoreAsync("2026-05-20-standard", new ScoreRecord("Anna", 45.0, 1000L, "h"));

        var json = await _store.GetCurrentAsync();
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.TryGetProperty("scores", out var scores)).IsTrue();
        await Assert.That(scores.ValueKind).IsEqualTo(JsonValueKind.Object);
    }

    [Test]
    public async Task GetCurrentAsync_JsonFormat_CamelCasePropertyNames()
    {
        await _store.AppendScoreAsync("key",
            new ScoreRecord("Anna", 45.0, 1000L, "h", HintsUsed: 2, WordHintsUsed: 1));

        var json = await _store.GetCurrentAsync();

        await Assert.That(json).Contains("\"hintsUsed\"");
        await Assert.That(json).Contains("\"wordHintsUsed\"");
        await Assert.That(json).Contains("\"puzzleHash\"");
        // Should NOT contain PascalCase
        await Assert.That(json).DoesNotContain("\"HintsUsed\"");
    }

    // -----------------------------------------------------------------------
    // DatePattern
    // -----------------------------------------------------------------------

    [Test]
    public async Task DatePattern_MatchesValidDate()
    {
        await Assert.That(LeaderboardStore.DatePattern.IsMatch("2026-05-20")).IsTrue();
    }

    [Test]
    public async Task DatePattern_RejectsInvalidFormats()
    {
        await Assert.That(LeaderboardStore.DatePattern.IsMatch("not-a-date")).IsFalse();
        await Assert.That(LeaderboardStore.DatePattern.IsMatch("2026/05/20")).IsFalse();
        await Assert.That(LeaderboardStore.DatePattern.IsMatch("20260520")).IsFalse();
        await Assert.That(LeaderboardStore.DatePattern.IsMatch("")).IsFalse();
    }

    // -----------------------------------------------------------------------
    // GetAnalyticsSummaryAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetAnalyticsSummaryAsync_EmptyDatabase_ReturnsZeros()
    {
        var summary = await _store.GetAnalyticsSummaryAsync();

        await Assert.That(summary.TotalCompletions).IsEqualTo(0);
        await Assert.That(summary.UniquePlayers).IsEqualTo(0);
        await Assert.That(summary.RegisteredUsers).IsEqualTo(0);
        await Assert.That(summary.CompletionsToday).IsEqualTo(0);
        await Assert.That(summary.ActiveToday).IsEqualTo(0);
        await Assert.That(summary.AverageTime).IsEqualTo(0);
        await Assert.That(summary.BestTime).IsEqualTo(0);
    }

    [Test]
    public async Task GetAnalyticsSummaryAsync_ReturnsCorrectAggregates()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var yesterday = GetSwedishDate().AddDays(-1).ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(today, new HistoryRecord("Anna", 40.0, 1000L, "h1", "17x17"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("Erik", 60.0, 2000L, "h1", "17x17", HintsUsed: 2));
        await _store.AppendHistoryAsync(yesterday, new HistoryRecord("Anna", 50.0, 3000L, "h2", "17x17"));

        var summary = await _store.GetAnalyticsSummaryAsync();

        await Assert.That(summary.TotalCompletions).IsEqualTo(3);
        await Assert.That(summary.UniquePlayers).IsEqualTo(2);
        await Assert.That(summary.BestTime).IsEqualTo(40.0);
        await Assert.That(summary.AverageTime).IsEqualTo(50.0);
    }

    [Test]
    public async Task GetAnalyticsSummaryAsync_HintRate_ComputedCorrectly()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(today, new HistoryRecord("A", 10.0, 1000L, "h", "s", HintsUsed: 1));
        await _store.AppendHistoryAsync(today, new HistoryRecord("B", 20.0, 2000L, "h", "s", HintsUsed: 0));
        await _store.AppendHistoryAsync(today, new HistoryRecord("C", 30.0, 3000L, "h", "s", HintsUsed: 0, WordHintsUsed: 3));

        var summary = await _store.GetAnalyticsSummaryAsync();

        // 2 out of 3 used any hint (A used hints, C used word hints) → 0.667
        await Assert.That(summary.HintUsageRate).IsEqualTo(0.667);
    }

    // -----------------------------------------------------------------------
    // GetDailyAnalyticsAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetDailyAnalyticsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _store.GetDailyAnalyticsAsync(30);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetDailyAnalyticsAsync_ReturnsPerDayBreakdown()
    {
        var today = GetSwedishDate();
        var todayStr = today.ToString("yyyy-MM-dd");
        var yesterdayStr = today.AddDays(-1).ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(todayStr, new HistoryRecord("Anna", 40.0, 1000L, "h1", "17x17"));
        await _store.AppendHistoryAsync(todayStr, new HistoryRecord("Erik", 60.0, 2000L, "h1", "17x17"));
        await _store.AppendHistoryAsync(yesterdayStr, new HistoryRecord("Anna", 30.0, 3000L, "h2", "17x17"));

        var result = await _store.GetDailyAnalyticsAsync(7);

        await Assert.That(result.Count).IsEqualTo(2);
        // Ordered by date DESC — today first
        await Assert.That(result[0].Date).IsEqualTo(todayStr);
        await Assert.That(result[0].Completions).IsEqualTo(2);
        await Assert.That(result[0].UniquePlayers).IsEqualTo(2);
        await Assert.That(result[0].AverageTime).IsEqualTo(50.0);
        await Assert.That(result[0].BestTime).IsEqualTo(40.0);

        await Assert.That(result[1].Date).IsEqualTo(yesterdayStr);
        await Assert.That(result[1].Completions).IsEqualTo(1);
    }

    [Test]
    public async Task GetDailyAnalyticsAsync_ExcludesOutOfRange()
    {
        var today = GetSwedishDate();
        var fiveDaysAgo = today.AddDays(-5).ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(fiveDaysAgo, new HistoryRecord("Old", 10.0, 1000L, "h", "s"));

        var result = await _store.GetDailyAnalyticsAsync(3);

        await Assert.That(result.Count).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // GetTopPlayersAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetTopPlayersAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var result = await _store.GetTopPlayersAsync(10);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetTopPlayersAsync_RankedByGamesPlayed()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");
        var yesterday = GetSwedishDate().AddDays(-1).ToString("yyyy-MM-dd");

        // Anna plays 3 games, Erik plays 1
        await _store.AppendHistoryAsync(today, new HistoryRecord("Anna", 40.0, 1000L, "h1", "17x17"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("Anna", 50.0, 2000L, "h2", "17x17"));
        await _store.AppendHistoryAsync(yesterday, new HistoryRecord("Anna", 35.0, 3000L, "h3", "17x17"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("Erik", 30.0, 4000L, "h1", "17x17"));

        var result = await _store.GetTopPlayersAsync(10);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].DisplayName).IsEqualTo("Anna");
        await Assert.That(result[0].GamesPlayed).IsEqualTo(3);
        await Assert.That(result[0].BestTime).IsEqualTo(35.0);
        await Assert.That(result[1].DisplayName).IsEqualTo("Erik");
        await Assert.That(result[1].GamesPlayed).IsEqualTo(1);
    }

    [Test]
    public async Task GetTopPlayersAsync_RespectsLimit()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        await _store.AppendHistoryAsync(today, new HistoryRecord("A", 10.0, 1000L, "h", "s"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("B", 20.0, 2000L, "h", "s"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("C", 30.0, 3000L, "h", "s"));

        var result = await _store.GetTopPlayersAsync(2);

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetTopPlayersAsync_TieBreaksByAverageTime()
    {
        var today = GetSwedishDate().ToString("yyyy-MM-dd");

        // Both play 1 game — Fast should rank first due to lower avg time
        await _store.AppendHistoryAsync(today, new HistoryRecord("Fast", 20.0, 1000L, "h", "s"));
        await _store.AppendHistoryAsync(today, new HistoryRecord("Slow", 80.0, 2000L, "h", "s"));

        var result = await _store.GetTopPlayersAsync(10);

        await Assert.That(result[0].DisplayName).IsEqualTo("Fast");
        await Assert.That(result[1].DisplayName).IsEqualTo("Slow");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static LeaderboardStore CreateStore(string dataDir)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:LeaderboardPath"] = dataDir
            })
            .Build();
        return new LeaderboardStore(config, NullLogger<LeaderboardStore>.Instance, TimeProvider.System, new TestHostEnvironment());
    }

    private static DateOnly GetSwedishDate()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // -----------------------------------------------------------------------
    // Friends — SendFriendRequestAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task SendFriendRequest_ToSelf_Fails()
    {
        var (success, _) = await _store.SendFriendRequestAsync("user1", "user1");
        await Assert.That(success).IsFalse();
    }

    [Test]
    public async Task SendFriendRequest_Success()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");

        var (success, _) = await _store.SendFriendRequestAsync("user1", "user2");
        await Assert.That(success).IsTrue();
    }

    [Test]
    public async Task SendFriendRequest_Duplicate_Fails()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");

        await _store.SendFriendRequestAsync("user1", "user2");
        var (success, _) = await _store.SendFriendRequestAsync("user1", "user2");
        await Assert.That(success).IsFalse();
    }

    [Test]
    public async Task SendFriendRequest_MutualPending_AutoAccepts()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");

        // A sends to B
        var (sent, _) = await _store.SendFriendRequestAsync("user1", "user2");
        await Assert.That(sent).IsTrue();

        // B sends to A — should auto-accept the existing request
        var (mutual, _) = await _store.SendFriendRequestAsync("user2", "user1");
        await Assert.That(mutual).IsTrue();

        // They should now be friends
        var friends = await _store.GetFriendsAsync("user1");
        await Assert.That(friends.Count).IsEqualTo(1);
        await Assert.That(friends[0].Alias).IsEqualTo("Bob");

        // No pending requests remain
        var pending = await _store.GetPendingRequestsAsync("user1");
        await Assert.That(pending.Count).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // Friends — AcceptFriendRequestAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task AcceptFriendRequest_Success()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");
        await _store.SendFriendRequestAsync("user1", "user2");

        var requests = await _store.GetPendingRequestsAsync("user2");
        await Assert.That(requests.Count).IsEqualTo(1);

        var accepted = await _store.AcceptFriendRequestAsync(requests[0].Id, "user2");
        await Assert.That(accepted).IsTrue();

        var friends = await _store.GetFriendsAsync("user2");
        await Assert.That(friends.Count).IsEqualTo(1);
        await Assert.That(friends[0].Alias).IsEqualTo("Alice");
    }

    [Test]
    public async Task AcceptFriendRequest_WrongUser_Fails()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");
        await _store.SendFriendRequestAsync("user1", "user2");

        var requests = await _store.GetPendingRequestsAsync("user2");
        var accepted = await _store.AcceptFriendRequestAsync(requests[0].Id, "user1");
        await Assert.That(accepted).IsFalse();
    }

    // -----------------------------------------------------------------------
    // Friends — DeclineFriendRequestAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task DeclineFriendRequest_SetsDeclinedStatus()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");
        await _store.SendFriendRequestAsync("user1", "user2");

        var requests = await _store.GetPendingRequestsAsync("user2");
        var declined = await _store.DeclineFriendRequestAsync(requests[0].Id, "user2");
        await Assert.That(declined).IsTrue();

        var remaining = await _store.GetPendingRequestsAsync("user2");
        await Assert.That(remaining.Count).IsEqualTo(0);

        // Re-sending should fail because declined row still exists
        var (success, _) = await _store.SendFriendRequestAsync("user1", "user2");
        await Assert.That(success).IsFalse();
    }

    // -----------------------------------------------------------------------
    // Friends — RemoveFriendAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task RemoveFriend_Success()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");
        await _store.SendFriendRequestAsync("user1", "user2");

        var requests = await _store.GetPendingRequestsAsync("user2");
        await _store.AcceptFriendRequestAsync(requests[0].Id, "user2");

        var friends = await _store.GetFriendsAsync("user2");
        await Assert.That(friends.Count).IsEqualTo(1);

        var removed = await _store.RemoveFriendAsync("user2", friends[0].FriendId);
        await Assert.That(removed).IsTrue();

        var remaining = await _store.GetFriendsAsync("user2");
        await Assert.That(remaining.Count).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // Friends — GetUserIdByAliasAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetUserIdByAlias_ReturnsCorrectUserId()
    {
        await _store.SetAliasAsync("user1", "Alice");
        var result = await _store.GetUserIdByAliasAsync("Alice");
        await Assert.That(result).IsEqualTo("user1");
    }

    [Test]
    public async Task GetUserIdByAlias_CaseInsensitive()
    {
        await _store.SetAliasAsync("user1", "Alice");
        var result = await _store.GetUserIdByAliasAsync("alice");
        await Assert.That(result).IsEqualTo("user1");
    }

    [Test]
    public async Task GetUserIdByAlias_NotFound_ReturnsNull()
    {
        var result = await _store.GetUserIdByAliasAsync("nobody");
        await Assert.That(result).IsNull();
    }

    // -----------------------------------------------------------------------
    // Friends — GetFriendsLeaderboardAsync
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetFriendsLeaderboard_IncludesSelfAndFriends()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");

        // Become friends
        await _store.SendFriendRequestAsync("user1", "user2");
        var requests = await _store.GetPendingRequestsAsync("user2");
        await _store.AcceptFriendRequestAsync(requests[0].Id, "user2");

        // Add history entries
        await _store.AppendHistoryAsync("2026-06-01", new HistoryRecord("Alice", 30.0, 1000L, "h1", "17x17", 0, 0, "user1"));
        await _store.AppendHistoryAsync("2026-06-01", new HistoryRecord("Bob", 45.0, 1001L, "h1", "17x17", 0, 0, "user2"));

        var lb = await _store.GetFriendsLeaderboardAsync("user1", "2026-06-01");
        await Assert.That(lb.Count).IsEqualTo(2);
        await Assert.That(lb[0].Name).IsEqualTo("Alice");
        await Assert.That(lb[1].Name).IsEqualTo("Bob");
    }

    [Test]
    public async Task GetFriendsLeaderboard_FiltersByPuzzleHash()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");

        await _store.SendFriendRequestAsync("user1", "user2");
        var requests = await _store.GetPendingRequestsAsync("user2");
        await _store.AcceptFriendRequestAsync(requests[0].Id, "user2");

        // Alice solved both sizes, Bob only the large one
        await _store.AppendHistoryAsync("2026-06-01", new HistoryRecord("Alice", 20.0, 1000L, "small_hash", "10x10", 0, 0, "user1"));
        await _store.AppendHistoryAsync("2026-06-01", new HistoryRecord("Alice", 50.0, 1001L, "big_hash", "17x17", 0, 0, "user1"));
        await _store.AppendHistoryAsync("2026-06-01", new HistoryRecord("Bob", 60.0, 1002L, "big_hash", "17x17", 0, 0, "user2"));

        // Without filter: all 3 entries
        var all = await _store.GetFriendsLeaderboardAsync("user1", "2026-06-01");
        await Assert.That(all.Count).IsEqualTo(3);

        // Filter by big_hash: only the 17x17 entries
        var big = await _store.GetFriendsLeaderboardAsync("user1", "2026-06-01", "big_hash");
        await Assert.That(big.Count).IsEqualTo(2);
        await Assert.That(big[0].Name).IsEqualTo("Alice");
        await Assert.That(big[1].Name).IsEqualTo("Bob");

        // Filter by small_hash: only Alice's 10x10
        var small = await _store.GetFriendsLeaderboardAsync("user1", "2026-06-01", "small_hash");
        await Assert.That(small.Count).IsEqualTo(1);
        await Assert.That(small[0].Name).IsEqualTo("Alice");
    }

    [Test]
    public async Task GetPendingRequests_ReturnsCorrectDirection()
    {
        await _store.SetAliasAsync("user1", "Alice");
        await _store.SetAliasAsync("user2", "Bob");
        await _store.SendFriendRequestAsync("user1", "user2");

        var forReceiver = await _store.GetPendingRequestsAsync("user2");
        await Assert.That(forReceiver.Count).IsEqualTo(1);
        await Assert.That(forReceiver[0].Direction).IsEqualTo("incoming");

        var forSender = await _store.GetPendingRequestsAsync("user1");
        await Assert.That(forSender.Count).IsEqualTo(1);
        await Assert.That(forSender[0].Direction).IsEqualTo("outgoing");
    }

    [Test]
    public async Task SendFriendRequest_TooManyPending_Fails()
    {
        await _store.SetAliasAsync("user1", "Spammer");
        for (int i = 0; i < 50; i++)
        {
            var targetId = $"target{i}";
            await _store.SetAliasAsync(targetId, $"User{i}");
            var (s, _) = await _store.SendFriendRequestAsync("user1", targetId);
            await Assert.That(s).IsTrue();
        }

        await _store.SetAliasAsync("target50", "User50");
        var (success, _) = await _store.SendFriendRequestAsync("user1", "target50");
        await Assert.That(success).IsFalse();
    }

    // -----------------------------------------------------------------------
    // GetUserStatsAsync — per-size stats
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetUserStatsAsync_ReturnsPerSizeStatsWithStreaks()
    {
        var today = GetSwedishDate();
        var d0 = today.ToString("yyyy-MM-dd");
        var d1 = today.AddDays(-1).ToString("yyyy-MM-dd");
        var d2 = today.AddDays(-2).ToString("yyyy-MM-dd");

        // User solves 10x10 on 3 consecutive days and 17x17 only today
        await _store.AppendHistoryAsync(d0, new HistoryRecord("A", 30.0, 100L, "h1", "10x10", UserId: "u1"));
        await _store.AppendHistoryAsync(d1, new HistoryRecord("A", 40.0, 101L, "h2", "10x10", UserId: "u1"));
        await _store.AppendHistoryAsync(d2, new HistoryRecord("A", 50.0, 102L, "h3", "10x10", UserId: "u1"));
        await _store.AppendHistoryAsync(d0, new HistoryRecord("A", 90.0, 103L, "h4", "17x17", UserId: "u1"));

        var stats = await _store.GetUserStatsAsync("u1");

        await Assert.That(stats.TotalSolved).IsEqualTo(4);
        // Overall streak = 3 consecutive days
        await Assert.That(stats.CurrentStreak).IsEqualTo(3);

        await Assert.That(stats.PerSize).IsNotNull();
        await Assert.That(stats.PerSize!.ContainsKey("10x10")).IsTrue();
        await Assert.That(stats.PerSize!.ContainsKey("17x17")).IsTrue();

        var small = stats.PerSize["10x10"];
        await Assert.That(small.Count).IsEqualTo(3);
        await Assert.That(small.BestTime).IsEqualTo(30.0);
        await Assert.That(small.AverageTime).IsEqualTo(40.0);
        await Assert.That(small.CurrentStreak).IsEqualTo(3);
        await Assert.That(small.BestStreak).IsEqualTo(3);

        var large = stats.PerSize["17x17"];
        await Assert.That(large.Count).IsEqualTo(1);
        await Assert.That(large.BestTime).IsEqualTo(90.0);
        await Assert.That(large.CurrentStreak).IsEqualTo(1);
        await Assert.That(large.BestStreak).IsEqualTo(1);
    }

    [Test]
    public async Task GetUserStatsAsync_EmptyHistory_ReturnsZeros()
    {
        var stats = await _store.GetUserStatsAsync("nonexistent");
        await Assert.That(stats.TotalSolved).IsEqualTo(0);
        await Assert.That(stats.PerSize).IsNull();
    }
}
