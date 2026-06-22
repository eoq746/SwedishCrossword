using TUnit.Assertions;
using TUnit.Core;

namespace SwedishCrossword.Api.Tests;

[Category("Unit")]
public class WordListMergerTombstoneTests
{
    [Test]
    public async Task LoadTombstonesFromJson_NormalizesAndDeDuplicatesWords()
    {
        var tombstones = WordListMerger.LoadTombstonesFromJson("""
            [" alpha ", "ALPHA", "beta", ""]
            """);

        await Assert.That(tombstones.Count).IsEqualTo(2);
        await Assert.That(tombstones.Contains("ALPHA")).IsTrue();
        await Assert.That(tombstones.Contains("beta")).IsTrue();
    }

    [Test]
    public async Task ApplyTombstones_RemovesOnlyTombstonedWords()
    {
        var mergedJson = """
            [
              { "word": "ALPHA", "clue": "Old", "alternativeClues": [] },
              { "word": "BETA", "clue": "Keep", "alternativeClues": [] }
            ]
            """;

        var tombstones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alpha"
        };

        var filtered = WordListMerger.ApplyTombstones(mergedJson, tombstones, "lexin-words.json");
        var map = WordListMerger.ParseWordMap(filtered, "lexin-words.json");

        await Assert.That(map.ContainsKey("ALPHA")).IsFalse();
        await Assert.That(map.ContainsKey("BETA")).IsTrue();
    }

    [Test]
    public async Task ApplyTombstones_WhenBaselineIsMissing_KeepsNewWordButBlocksDeletedLegacyWord()
    {
        var devJson = """
            [
              { "word": "ALPHA", "clue": "Legacy", "alternativeClues": [] },
              { "word": "GAMMA", "clue": "Brand new", "alternativeClues": [] }
            ]
            """;
        var prodJson = "[]";

        // Simulates baseline loss recovery path where base falls back to prod.
        var merge = WordListMerger.MergeThreeWay(prodJson, devJson, prodJson, "lexin-words.json");

        var tombstones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ALPHA"
        };

        var filtered = WordListMerger.ApplyTombstones(merge.MergedJson, tombstones, "lexin-words.json");
        var map = WordListMerger.ParseWordMap(filtered, "lexin-words.json");

        await Assert.That(map.ContainsKey("ALPHA")).IsFalse();
        await Assert.That(map.ContainsKey("GAMMA")).IsTrue();
    }
}
