using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services.Generation;

namespace SwedishCrossword.Tests;

[Category("Unit")]
/// <summary>
/// Tests for the internal <see cref="GenerationHelpers"/> utility methods.
/// Requires InternalsVisibleTo from SwedishCrossword.Core.
/// </summary>
public class GenerationHelpersTests
{
    // -----------------------------------------------------------------------
    // CountVowels
    // -----------------------------------------------------------------------

    [Test, Category("Unit"), Category("Validation")]
    [Arguments("AEIOU", 5, DisplayName = "CountVowels counts latin vowels")]
    [Arguments("ÅÄÖ", 3, DisplayName = "CountVowels counts Swedish vowels")]
    [Arguments("STÖRTLOPP", 2, DisplayName = "CountVowels counts mixed text vowels")]
    [Arguments("BCDG", 0, DisplayName = "CountVowels returns zero for consonants only")]
    [Arguments("", 0, DisplayName = "CountVowels returns zero for empty string")]
    public async Task CountVowels_ReturnsExpectedCount(string text, int expectedCount)
    {
        var count = GenerationHelpers.CountVowels(text);
        await Assert.That(count).IsEqualTo(expectedCount);
    }

    // -----------------------------------------------------------------------
    // FindWordsMatchingPattern
    // -----------------------------------------------------------------------

    [Test]
    public async Task FindWordsMatchingPattern_MatchesWildcardPattern()
    {
        var words = new List<Word>
        {
            new("CAT", "Animal"),
            new("BAT", "Nocturnal"),
            new("DOG", "Pet")
        };
        var pattern = new List<char?> { null, 'A', 'T' }; // _AT

        var matches = GenerationHelpers.FindWordsMatchingPattern(words, pattern, [], []);

        await Assert.That(matches.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FindWordsMatchingPattern_ExcludesPlacedWords()
    {
        var words = new List<Word>
        {
            new("CAT", "Animal"),
            new("BAT", "Nocturnal")
        };
        var pattern = new List<char?> { null, 'A', 'T' };

        var matches = GenerationHelpers.FindWordsMatchingPattern(
            words, pattern, new HashSet<string> { "CAT" }, []);

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].Text).IsEqualTo("BAT");
    }

    [Test]
    public async Task FindWordsMatchingPattern_ExcludesUsedWords()
    {
        var words = new List<Word>
        {
            new("CAT", "Animal"),
            new("BAT", "Nocturnal")
        };
        var pattern = new List<char?> { null, 'A', 'T' };

        var matches = GenerationHelpers.FindWordsMatchingPattern(
            words, pattern, [], new HashSet<string> { "BAT" });

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].Text).IsEqualTo("CAT");
    }

    [Test]
    public async Task FindWordsMatchingPattern_RejectsWrongLength()
    {
        var words = new List<Word>
        {
            new("CATS", "Animals"),
            new("BAT", "Nocturnal")
        };
        var pattern = new List<char?> { null, 'A', 'T' }; // length 3

        var matches = GenerationHelpers.FindWordsMatchingPattern(words, pattern, [], []);

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].Text).IsEqualTo("BAT");
    }

    [Test]
    public async Task FindWordsMatchingPattern_AllWildcards_MatchesAll()
    {
        var words = new List<Word>
        {
            new("CAT", "Animal"),
            new("DOG", "Pet")
        };
        var pattern = new List<char?> { null, null, null };

        var matches = GenerationHelpers.FindWordsMatchingPattern(words, pattern, [], []);

        await Assert.That(matches.Count).IsEqualTo(2);
    }

    [Test]
    public async Task FindWordsMatchingPattern_FullySpecified()
    {
        var words = new List<Word>
        {
            new("CAT", "Animal"),
            new("DOG", "Pet")
        };
        var pattern = new List<char?> { 'C', 'A', 'T' };

        var matches = GenerationHelpers.FindWordsMatchingPattern(words, pattern, [], []);

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0].Text).IsEqualTo("CAT");
    }

    [Test]
    public async Task FindWordsMatchingPattern_NoMatches()
    {
        var words = new List<Word>
        {
            new("CAT", "Animal"),
            new("DOG", "Pet")
        };
        var pattern = new List<char?> { 'Z', null, null };

        var matches = GenerationHelpers.FindWordsMatchingPattern(words, pattern, [], []);

        await Assert.That(matches.Count).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // ShuffleTopBiased
    // -----------------------------------------------------------------------

    [Test]
    public async Task ShuffleTopBiased_PreservesAllElements()
    {
        var list = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var original = new List<int>(list);

        GenerationHelpers.ShuffleTopBiased(list, 3, new Random(42));

        await Assert.That(list.Count).IsEqualTo(original.Count);
        await Assert.That(list.OrderBy(x => x).ToList()).IsEquivalentTo(original.OrderBy(x => x).ToList());
    }

    [Test]
    public async Task ShuffleTopBiased_SingleElement_NoChange()
    {
        var list = new List<int> { 42 };

        GenerationHelpers.ShuffleTopBiased(list, 3, new Random(0));

        await Assert.That(list[0]).IsEqualTo(42);
    }

    [Test]
    public async Task ShuffleTopBiased_EmptyList_NoException()
    {
        var list = new List<int>();

        GenerationHelpers.ShuffleTopBiased(list, 3, new Random(0));

        await Assert.That(list.Count).IsEqualTo(0);
    }

    // -----------------------------------------------------------------------
    // GetPreferredDirection
    // -----------------------------------------------------------------------

    [Test]
    public async Task GetPreferredDirection_MoreDown_PrefersAcross()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 0, 0, Direction.Down);
        grid.TryPlaceWord(new Word("DOG", "Pet"), 0, 2, Direction.Down);
        grid.TryPlaceWord(new Word("HAT", "Headwear"), 0, 4, Direction.Across);

        var preferred = GenerationHelpers.GetPreferredDirection(grid);

        await Assert.That(preferred).IsEqualTo(Direction.Across);
    }

    [Test]
    public async Task GetPreferredDirection_MoreAcross_PrefersDown()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("DOG", "Pet"), 2, 0, Direction.Across);
        grid.TryPlaceWord(new Word("HAT", "Headwear"), 0, 4, Direction.Down);

        var preferred = GenerationHelpers.GetPreferredDirection(grid);

        await Assert.That(preferred).IsEqualTo(Direction.Down);
    }

    [Test]
    public async Task GetPreferredDirection_Equal_PrefersAcross()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("DOG", "Pet"), 0, 4, Direction.Down);

        var preferred = GenerationHelpers.GetPreferredDirection(grid);

        await Assert.That(preferred).IsEqualTo(Direction.Across);
    }

    // -----------------------------------------------------------------------
    // CountNearbyWords
    // -----------------------------------------------------------------------

    [Test]
    public async Task CountNearbyWords_FindsLettersInRadius()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 2, 2, Direction.Across);

        // Count letters around (2, 3) — the 'A' in CAT — radius 1
        // Cells (2,2)='C', (2,4)='T' are neighbors with letters
        var count = GenerationHelpers.CountNearbyWords(grid, 2, 3, 1);

        await Assert.That(count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task CountNearbyWords_EmptyGrid_ReturnsZero()
    {
        var grid = new CrosswordGrid(10, 10);

        var count = GenerationHelpers.CountNearbyWords(grid, 5, 5, 2);

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task CountNearbyWords_ExcludesCenterCell()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("A", "Letter"), 5, 5, Direction.Across);

        // The center cell itself should be excluded
        var count = GenerationHelpers.CountNearbyWords(grid, 5, 5, 0);

        await Assert.That(count).IsEqualTo(0);
    }
}
