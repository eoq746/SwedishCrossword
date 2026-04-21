using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for multi-clue support (alternative clues per word)
/// </summary>
public class AlternativeCluesTests
{
    [Test]
    public async Task Word_Constructor_DefaultAlternativeCluesIsEmpty()
    {
        var word = new Word("KATT", "Husdjur");

        await Assert.That(word.AlternativeClues).IsEmpty();
    }

    [Test]
    public async Task Word_Constructor_SetsAlternativeClues()
    {
        var alts = new List<string> { "Mjauande djur", "Mössens fiende" };
        var word = new Word("KATT", "Husdjur", alternativeClues: alts);

        await Assert.That(word.AlternativeClues).Count().IsEqualTo(2);
        await Assert.That(word.AlternativeClues[0]).IsEqualTo("Mjauande djur");
        await Assert.That(word.AlternativeClues[1]).IsEqualTo("Mössens fiende");
    }

    [Test]
    public async Task Word_GetRandomClue_ReturnsPrimaryClueWhenNoAlternatives()
    {
        var word = new Word("KATT", "Husdjur");

        var clue = word.GetRandomClue();

        await Assert.That(clue).IsEqualTo("Husdjur");
    }

    [Test]
    public async Task Word_GetRandomClue_ReturnsOneOfAvailableClues()
    {
        var alts = new List<string> { "Mjauande djur", "Mössens fiende" };
        var word = new Word("KATT", "Husdjur", alternativeClues: alts);

        var allPossible = new HashSet<string> { "Husdjur", "Mjauande djur", "Mössens fiende" };

        // Call multiple times to verify it always returns a valid clue
        for (int i = 0; i < 50; i++)
        {
            var clue = word.GetRandomClue();
            await Assert.That(allPossible.Contains(clue)).IsTrue();
        }
    }

    [Test]
    public async Task Word_GetRandomClue_EventuallyReturnsMultipleDistinctClues()
    {
        var alts = new List<string> { "Mjauande djur", "Mössens fiende" };
        var word = new Word("KATT", "Husdjur", alternativeClues: alts);

        var seen = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            seen.Add(word.GetRandomClue());
        }

        // With 3 options and 200 tries, we should see more than 1 distinct clue
        await Assert.That(seen.Count).IsGreaterThan(1);
    }

    [Test]
    public async Task Dictionary_GetRandomClue_ReturnsPrimaryWhenNoAlternatives()
    {
        var dict = new SwedishDictionary(empty: true);
        dict.AddWord("KATT", "Husdjur", "Djur");

        var clue = dict.GetRandomClue("KATT");

        await Assert.That(clue).IsEqualTo("Husdjur");
    }

    [Test]
    public async Task Dictionary_GetRandomClue_ReturnsNullForMissingWord()
    {
        var dict = new SwedishDictionary(empty: true);

        var clue = dict.GetRandomClue("MISSING");

        await Assert.That(clue).IsNull();
    }

    [Test]
    public async Task Dictionary_GetClue_StillReturnsPrimaryClue()
    {
        var dict = new SwedishDictionary(empty: true);
        dict.AddWord("KATT", "Husdjur", "Djur");

        var clue = dict.GetClue("KATT");

        await Assert.That(clue).IsEqualTo("Husdjur");
    }

    [Test]
    public async Task WordEntry_AlternativeClues_DeserializedFromJson()
    {
        var json = """
        [{"Word":"KATT","Clue":"Husdjur","AlternativeClues":["Mjauande djur","Mössens fiende"],"Category":"Djur"}]
        """;

        var entries = System.Text.Json.JsonSerializer.Deserialize<List<WordEntry>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        await Assert.That(entries).IsNotNull();
        await Assert.That(entries![0].AlternativeClues).Count().IsEqualTo(2);
        await Assert.That(entries[0].AlternativeClues[0]).IsEqualTo("Mjauande djur");
    }

    [Test]
    public async Task WordEntry_AlternativeClues_DefaultsToEmptyWhenMissing()
    {
        var json = """
        [{"Word":"KATT","Clue":"Husdjur"}]
        """;

        var entries = System.Text.Json.JsonSerializer.Deserialize<List<WordEntry>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        await Assert.That(entries).IsNotNull();
        await Assert.That(entries![0].AlternativeClues).IsEmpty();
    }

    [Test]
    public async Task ConvertToWord_PreservesAlternativeClues()
    {
        var dict = new SwedishDictionary(empty: true);
        dict.AddWord("KATT", "Husdjur", "Djur");

        // The word from AllWords should have AlternativeClues (empty in this case)
        var word = dict.AllWords.First(w => w.Text == "KATT");
        await Assert.That(word.AlternativeClues).IsNotNull();
    }
}
