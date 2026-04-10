using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the SwedishDictionary class
/// </summary>
public class SwedishDictionaryTests
{
    [Test]
    public async Task Constructor_CreatesEmptyDictionaryWhenRequested()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(dictionary.WordCount).IsEqualTo(0);
        await Assert.That(dictionary.AllWords).IsEmpty();
    }

    [Test]
    public async Task AddWord_IncreasesWordCount()
    {
        var dictionary = new SwedishDictionary(empty: true);

        dictionary.AddWord("TEST", "A test word", "Category");

        await Assert.That(dictionary.WordCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddWord_MakesWordValid()
    {
        var dictionary = new SwedishDictionary(empty: true);

        dictionary.AddWord("TESTORD", "Ett testord", "Test", DifficultyLevel.Easy);

        await Assert.That(dictionary.IsValidWord("TESTORD")).IsTrue();
    }

    [Test]
    public async Task AddWord_ThrowsForEmptyWord()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(() => dictionary.AddWord("", "Valid clue", "Test"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddWord_ThrowsForWhitespaceWord()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(() => dictionary.AddWord("   ", "Valid clue", "Test"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddWord_ThrowsForEmptyClue()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(() => dictionary.AddWord("WORD", "", "Test"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddWord_ThrowsForWhitespaceClue()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(() => dictionary.AddWord("WORD", "   ", "Test"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AddWord_ThrowsForDuplicateWord()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("UNIQUE", "First version", "Test");

        await Assert.That(() => dictionary.AddWord("UNIQUE", "Second version", "Test"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddWord_ThrowsForCaseInsensitiveDuplicate()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("WORD", "First", "Test");

        await Assert.That(() => dictionary.AddWord("word", "Second", "Test"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IsValidWord_ReturnsFalseForNonexistentWord()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(dictionary.IsValidWord("XYZABC123")).IsFalse();
    }

    [Test]
    public async Task IsValidWord_ReturnsFalseForEmptyString()
    {
        var dictionary = new SwedishDictionary(empty: true);

        await Assert.That(dictionary.IsValidWord("")).IsFalse();
    }

    [Test]
    public async Task IsValidWord_IsCaseInsensitive()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("TESTORD", "Test", "Test");

        await Assert.That(dictionary.IsValidWord("TESTORD")).IsTrue();
        await Assert.That(dictionary.IsValidWord("testord")).IsTrue();
        await Assert.That(dictionary.IsValidWord("Testord")).IsTrue();
        await Assert.That(dictionary.IsValidWord("TeStOrD")).IsTrue();
    }

    [Test]
    public async Task IsValidWord_ReturnsFalseForPlaceholderClue()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("TESTORD", "___", "Test");

        await Assert.That(dictionary.IsValidWord("TESTORD")).IsFalse();
    }

    [Test]
    public async Task GetWords_FiltersByMinLength()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Two", "Test");
        dictionary.AddWord("ABC", "Three", "Test");
        dictionary.AddWord("ABCD", "Four", "Test");

        var words = dictionary.GetWords(minLength: 3).ToList();

        await Assert.That(words.Count).IsEqualTo(2);
        await Assert.That(words.All(w => w.Length >= 3)).IsTrue();
    }

    [Test]
    public async Task GetWords_FiltersByMaxLength()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Two", "Test");
        dictionary.AddWord("ABC", "Three", "Test");
        dictionary.AddWord("ABCD", "Four", "Test");

        var words = dictionary.GetWords(maxLength: 3).ToList();

        await Assert.That(words.Count).IsEqualTo(2);
        await Assert.That(words.All(w => w.Length <= 3)).IsTrue();
    }

    [Test]
    public async Task GetWords_FiltersByLengthRange()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Two", "Test");
        dictionary.AddWord("ABC", "Three", "Test");
        dictionary.AddWord("ABCDE", "Five", "Test");

        var words = dictionary.GetWords(minLength: 2, maxLength: 3).ToList();

        await Assert.That(words.Count).IsEqualTo(2);
        await Assert.That(words.All(w => w.Length >= 2 && w.Length <= 3)).IsTrue();
    }

    [Test]
    public async Task GetWords_FiltersByCategory()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("KATT", "Cat", "Animals");
        dictionary.AddWord("HUND", "Dog", "Animals");
        dictionary.AddWord("BIL", "Car", "Vehicles");

        var words = dictionary.GetWords(category: "Animals").ToList();

        await Assert.That(words.Count).IsEqualTo(2);
        await Assert.That(words.All(w => w.Category == "Animals")).IsTrue();
    }

    [Test]
    public async Task GetWords_FiltersByDifficulty()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("EASY", "Easy", "Test", DifficultyLevel.Easy);
        dictionary.AddWord("MEDIUM", "Med", "Test", DifficultyLevel.Medium);
        dictionary.AddWord("HARD", "Hard", "Test", DifficultyLevel.Hard);

        var words = dictionary.GetWords(difficulty: DifficultyLevel.Easy).ToList();

        await Assert.That(words.Count).IsEqualTo(1);
        await Assert.That(words[0].Difficulty).IsEqualTo(DifficultyLevel.Easy);
    }

    [Test]
    public async Task GetWordsWithLetter_FindsWordsContainingLetter()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("KATT", "Cat", "Animals");
        dictionary.AddWord("HUND", "Dog", "Animals");
        dictionary.AddWord("FISK", "Fish", "Animals");

        var words = dictionary.GetWordsWithLetter('K').ToList();

        await Assert.That(words.Count).IsEqualTo(2);
        await Assert.That(words.All(w => w.Text.Contains('K'))).IsTrue();
    }

    [Test]
    public async Task GetWordsWithLetterAt_FindsWordsWithLetterAtPosition()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("KATT", "Cat", "Animals");
        dictionary.AddWord("KOPP", "Cup", "Objects");
        dictionary.AddWord("BOLL", "Ball", "Objects");

        var words = dictionary.GetWordsWithLetterAt('K', 0).ToList();

        await Assert.That(words.Count).IsEqualTo(2);
        await Assert.That(words.All(w => w.Text[0] == 'K')).IsTrue();
    }

    [Test]
    public async Task GetRandomWords_ReturnsRequestedCount()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("ETT", "One", "Numbers");
        dictionary.AddWord("TVÅ", "Two", "Numbers");
        dictionary.AddWord("TRE", "Three", "Numbers");
        dictionary.AddWord("FYRA", "Four", "Numbers");
        dictionary.AddWord("FEM", "Five", "Numbers");

        var words = dictionary.GetRandomWords(3).ToList();

        await Assert.That(words.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetRandomWords_ReturnsNoDuplicates()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("ETT", "One", "Numbers");
        dictionary.AddWord("TVÅ", "Two", "Numbers");
        dictionary.AddWord("TRE", "Three", "Numbers");

        var words = dictionary.GetRandomWords(3).ToList();

        var uniqueCount = words.Select(w => w.Text).Distinct().Count();
        await Assert.That(uniqueCount).IsEqualTo(words.Count);
    }

    [Test]
    public async Task GetRandomWords_ReturnsAllAvailableWhenCountExceeds()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("ETT", "One", "Numbers");
        dictionary.AddWord("TVÅ", "Two", "Numbers");

        var words = dictionary.GetRandomWords(10).ToList();

        await Assert.That(words.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetRandomWords_RespectsExclusionList()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("ETT", "One", "Numbers");
        dictionary.AddWord("TVÅ", "Two", "Numbers");
        dictionary.AddWord("TRE", "Three", "Numbers");

        var excludeWord = dictionary.AllWords.First(w => w.Text == "ETT");
        var words = dictionary.GetRandomWords(3, [excludeWord]).ToList();

        await Assert.That(words.All(w => w.Text != "ETT")).IsTrue();
    }

    [Test]
    public async Task GetStarterWords_ReturnsWordsWithCommonLetters()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AARE", "River", "Places");
        dictionary.AddWord("QXZW", "Weird", "Test");
        dictionary.AddWord("SNAR", "Fast", "Adjectives");

        var words = dictionary.GetStarterWords(maxLength: 6).ToList();

        await Assert.That(words).IsNotEmpty();
        
        var commonLetters = new HashSet<char> { 'A', 'E', 'I', 'O', 'U', 'R', 'S', 'T', 'N', 'L' };
        await Assert.That(words.All(w => w.Text.Any(c => commonLetters.Contains(c)))).IsTrue();
    }

    [Test]
    public async Task CreateWord_ReturnsValidWordInstance()
    {
        var word = SwedishDictionary.CreateWord("TEST", "A test", "Category", DifficultyLevel.Hard);

        await Assert.That(word.Text).IsEqualTo("TEST");
        await Assert.That(word.Clue).IsEqualTo("A test");
        await Assert.That(word.Category).IsEqualTo("Category");
        await Assert.That(word.Difficulty).IsEqualTo(DifficultyLevel.Hard);
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task GetStatistics_ReturnsCorrectTotalWords()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Short", "Test", DifficultyLevel.Easy);
        dictionary.AddWord("ABC", "Medium", "Test", DifficultyLevel.Medium);
        dictionary.AddWord("ABCD", "Longer", "Other", DifficultyLevel.Hard);

        var stats = dictionary.GetStatistics();

        await Assert.That(stats.TotalWords).IsEqualTo(3);
    }

    [Test]
    public async Task GetStatistics_ReturnsCorrectCategories()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("A", "One", "Test");
        dictionary.AddWord("B", "Two", "Test");
        dictionary.AddWord("C", "Three", "Other");

        var stats = dictionary.GetStatistics();

        await Assert.That(stats.Categories.Count).IsEqualTo(2);
        await Assert.That(stats.Categories["Test"]).IsEqualTo(2);
        await Assert.That(stats.Categories["Other"]).IsEqualTo(1);
    }

    [Test]
    public async Task GetStatistics_ReturnsCorrectLengthDistribution()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Two", "Test");
        dictionary.AddWord("ABC", "Three", "Test");
        dictionary.AddWord("ABCD", "Four", "Test");

        var stats = dictionary.GetStatistics();

        await Assert.That(stats.LengthDistribution.Count).IsEqualTo(3);
        await Assert.That(stats.LengthDistribution[2]).IsEqualTo(1);
        await Assert.That(stats.LengthDistribution[3]).IsEqualTo(1);
        await Assert.That(stats.LengthDistribution[4]).IsEqualTo(1);
    }

    [Test]
    public async Task GetStatistics_CalculatesAverageLength()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Two", "Test");
        dictionary.AddWord("ABC", "Three", "Test");
        dictionary.AddWord("ABCD", "Four", "Test");

        var stats = dictionary.GetStatistics();

        await Assert.That(stats.AverageLength).IsEqualTo(3.0);
    }

    [Test]
    public async Task GetStatistics_FindsMinAndMaxLength()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Two", "Test");
        dictionary.AddWord("ABCDEF", "Six", "Test");

        var stats = dictionary.GetStatistics();

        await Assert.That(stats.MinLength).IsEqualTo(2);
        await Assert.That(stats.MaxLength).IsEqualTo(6);
    }

    [Test]
    public async Task GetStatistics_ReturnsEmptyStatsForEmptyDictionary()
    {
        var dictionary = new SwedishDictionary(empty: true);

        var stats = dictionary.GetStatistics();

        await Assert.That(stats.TotalWords).IsEqualTo(0);
        await Assert.That(stats.Categories).IsEmpty();
        await Assert.That(stats.AverageLength).IsEqualTo(0);
    }

    [Test]
    public async Task FindIntersectingWords_FindsWordsWithSharedLetter()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("KATT", "Cat", "Animals");
        dictionary.AddWord("ARM", "Arm", "Body");
        dictionary.AddWord("BOLL", "Ball", "Objects");

        var targetWord = new Word("KATT", "Cat");
        var intersecting = dictionary.FindIntersectingWords(targetWord, 'A').ToList();

        await Assert.That(intersecting.Count).IsEqualTo(1);
        await Assert.That(intersecting[0].Text).IsEqualTo("ARM");
    }
}
