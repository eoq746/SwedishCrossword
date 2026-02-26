using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the GridValidator class
/// </summary>
public class GridValidatorTests
{
    private GridValidator _validator = null!;

    [Before(Test)]
    public void Setup()
    {
        _validator = new GridValidator();
    }

    [Test]
    public async Task IsValidCrossword_ReturnsTrueForValidGrid()
    {
        var grid = new CrosswordGrid(10, 10);
        var word1 = new Word("CAT", "Animal");
        var word2 = new Word("ACE", "Card");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 3, Direction.Down);

        var isValid = _validator.IsValidCrossword(grid);

        await Assert.That(isValid).IsTrue();
    }

    [Test]
    public async Task IsValidCrossword_ReturnsFalseForEmptyGrid()
    {
        var grid = new CrosswordGrid(5, 5);

        var isValid = _validator.IsValidCrossword(grid);

        await Assert.That(isValid).IsFalse();
    }

    [Test]
    public async Task ValidateGrid_ReturnsValidResultForConnectedWords()
    {
        var grid = new CrosswordGrid(10, 10);
        var word1 = new Word("CAT", "Animal");
        var word2 = new Word("ACE", "Card");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 3, Direction.Down);

        var result = _validator.ValidateGrid(grid);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task ValidateGrid_ReportsNoWordsError()
    {
        var grid = new CrosswordGrid(5, 5);

        var result = _validator.ValidateGrid(grid);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task CanPlaceWordSafely_ReturnsTrueForValidPlacement()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");

        var canPlace = _validator.CanPlaceWordSafely(grid, word, 0, 0, Direction.Across);

        await Assert.That(canPlace).IsTrue();
    }

    [Test]
    public async Task CanPlaceWordSafely_ReturnsFalseWhenOutOfBounds()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var canPlace = _validator.CanPlaceWordSafely(grid, word, 0, 0, Direction.Across);

        await Assert.That(canPlace).IsFalse();
    }

    [Test]
    public async Task CanPlaceWordSafely_ReturnsTrueForValidIntersection()
    {
        var grid = new CrosswordGrid(10, 10);
        var word1 = new Word("UTE", "OMODERN");
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);

        var word2 = new Word("TRO", "ANA");
        var canPlace = _validator.CanPlaceWordSafely(grid, word2, 2, 3, Direction.Down);

        await Assert.That(canPlace).IsTrue();
    }

    [Test]
    public async Task CanPlaceWordSafely_ReturnsFalseForConflictingLetters()
    {
        var grid = new CrosswordGrid(10, 10);
        var word1 = new Word("CAT", "Animal");
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);

        var word2 = new Word("DOG", "Pet");
        var canPlace = _validator.CanPlaceWordSafely(grid, word2, 2, 2, Direction.Across);

        await Assert.That(canPlace).IsFalse();
    }

    [Test]
    public async Task CanPlaceWordSafelyWithValidation_ChecksDictionary()
    {
        var grid = new CrosswordGrid(10, 10);
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("UTE", "OMODERN", "Test");
        dictionary.AddWord("TRO", "ANA", "Test");

        var word1 = new Word("UTE", "OMODERN");
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);

        var word2 = new Word("TRO", "ANA");
        var canPlace = _validator.CanPlaceWordSafelyWithValidation(
            grid, word2, 2, 3, Direction.Down, dictionary, rejectInvalidWords: true);

        await Assert.That(canPlace).IsTrue();
    }
}

/// <summary>
/// Unit tests for the ValidationResult class
/// </summary>
public class ValidationResultTests
{
    [Test]
    public async Task NewValidationResult_IsValid()
    {
        var result = new ValidationResult();

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.Warnings).IsEmpty();
        await Assert.That(result.Info).IsEmpty();
    }

    [Test]
    public async Task AddError_MakesResultInvalid()
    {
        var result = new ValidationResult();

        result.AddError("Test error");

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(1);
        await Assert.That(result.Errors[0]).Contains("Test error");
    }

    [Test]
    public async Task AddWarning_DoesNotMakeResultInvalid()
    {
        var result = new ValidationResult();

        result.AddWarning("Test warning");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Warnings.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddInfo_DoesNotMakeResultInvalid()
    {
        var result = new ValidationResult();

        result.AddInfo("Test info");

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Info.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ToString_ContainsAllMessages()
    {
        var result = new ValidationResult();
        result.AddError("Error message");
        result.AddWarning("Warning message");
        result.AddInfo("Info message");

        var output = result.ToString();

        await Assert.That(output).Contains("Error message");
        await Assert.That(output).Contains("Warning message");
        await Assert.That(output).Contains("Info message");
    }
}
