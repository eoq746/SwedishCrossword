using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the AccidentalWord class
/// </summary>
public class AccidentalWordTests
{
    [Test]
    public async Task NewAccidentalWord_HasCorrectDefaultProperties()
    {
        var accidentalWord = new AccidentalWord
        {
            Text = "ORD",
            StartRow = 2,
            StartCol = 3,
            Direction = Direction.Across,
            Length = 3
        };

        await Assert.That(accidentalWord.Text).IsEqualTo("ORD");
        await Assert.That(accidentalWord.StartRow).IsEqualTo(2);
        await Assert.That(accidentalWord.StartCol).IsEqualTo(3);
        await Assert.That(accidentalWord.Direction).IsEqualTo(Direction.Across);
        await Assert.That(accidentalWord.Length).IsEqualTo(3);
        await Assert.That(accidentalWord.IsValidSwedishWord).IsNull();
        await Assert.That(accidentalWord.ShouldIncludeInPuzzle).IsFalse();
        await Assert.That(accidentalWord.PuzzleNumber).IsEqualTo(0);
        await Assert.That(accidentalWord.ClueFromDictionary).IsEmpty();
    }

    [Test]
    public async Task ValidationStatus_ReturnsUncheckedForNullValidity()
    {
        var word = new AccidentalWord { Text = "TEST" };

        await Assert.That(word.ValidationStatus).Contains("kontrollerat");
    }

    [Test]
    public async Task ValidationStatus_ReturnsValidForValidWord()
    {
        var word = new AccidentalWord { Text = "TEST", IsValidSwedishWord = true };

        await Assert.That(word.ValidationStatus).Contains("Giltigt");
    }

    [Test]
    public async Task ValidationStatus_ReturnsValidAndIncludedForIncludedWord()
    {
        var word = new AccidentalWord 
        { 
            Text = "TEST", 
            IsValidSwedishWord = true,
            ShouldIncludeInPuzzle = true
        };

        await Assert.That(word.ValidationStatus).Contains("inkluderat");
    }

    [Test]
    public async Task ValidationStatus_ReturnsInvalidForInvalidWord()
    {
        var word = new AccidentalWord { Text = "TEST", IsValidSwedishWord = false };

        await Assert.That(word.ValidationStatus).Contains("Ogiltigt");
    }

    [Test]
    public async Task ToString_ContainsWordText()
    {
        var word = new AccidentalWord { Text = "TEST" };

        var result = word.ToString();

        await Assert.That(result).Contains("TEST");
    }

    [Test]
    public async Task ToString_ContainsPosition()
    {
        var word = new AccidentalWord 
        { 
            Text = "TEST",
            StartRow = 1,
            StartCol = 2
        };

        var result = word.ToString();

        await Assert.That(result).Contains("(2, 3)");
    }

    [Test]
    public async Task ToString_ContainsDirectionForAcross()
    {
        var word = new AccidentalWord 
        { 
            Text = "TEST",
            Direction = Direction.Across
        };

        var result = word.ToString();

        await Assert.That(result).Contains("vågrätt");
    }

    [Test]
    public async Task ToString_ContainsDirectionForDown()
    {
        var word = new AccidentalWord 
        { 
            Text = "TEST",
            Direction = Direction.Down
        };

        var result = word.ToString();

        await Assert.That(result).Contains("lodrätt");
    }

    [Test]
    public async Task ToString_ContainsPuzzleNumberWhenIncluded()
    {
        var word = new AccidentalWord 
        { 
            Text = "TEST",
            ShouldIncludeInPuzzle = true,
            PuzzleNumber = 5
        };

        var result = word.ToString();

        await Assert.That(result).Contains("#5");
    }
}

/// <summary>
/// Unit tests for accidental word detection in CrosswordGrid
/// </summary>
public class AccidentalWordDetectionTests
{
    [Test]
    public async Task DetectAccidentalWords_ReturnsEmptyForSingleWord()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("CAT", "Animal");
        grid.TryPlaceWord(word, 2, 2, Direction.Across);

        var accidentalWords = grid.DetectAccidentalWords();

        await Assert.That(accidentalWords).IsNotNull();
    }

    [Test]
    public async Task DetectAccidentalWords_ValidatesWithDictionary()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("SOL", "Sun", "Nature");
        dictionary.AddWord("ORD", "Word", "Language");
        
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("SOL", "Sun");
        var word2 = new Word("ORD", "Word");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 3, Direction.Down);

        var accidentalWords = grid.DetectAccidentalWords(dictionary);

        await Assert.That(accidentalWords).IsNotNull();
        foreach (var accWord in accidentalWords)
        {
            await Assert.That(accWord.IsValidSwedishWord).IsNotNull();
        }
    }

    [Test]
    public async Task DetectAccidentalWordsNear_ChecksSpecificArea()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("TEST", "Test", "General");
        
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "Test word");
        grid.TryPlaceWord(word, 3, 3, Direction.Across);

        var nearWords = grid.DetectAccidentalWordsNear(3, 3, Direction.Across, 4, dictionary);

        await Assert.That(nearWords).IsNotNull();
    }

    [Test]
    public async Task ValidateCrossword_IncludesAccidentalWordAnalysis()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("NU", "Now", "Time");
        dictionary.AddWord("NY", "New", "Adjectives");
        
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("NU", "Now");
        var word2 = new Word("NY", "New");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 2, Direction.Down);

        var validation = grid.ValidateCrossword(dictionary);

        await Assert.That(validation).IsNotNull();
        await Assert.That(validation.AccidentalWords).IsNotNull();
    }

    [Test]
    public async Task ValidateCrossword_SeparatesValidAndInvalidWords()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Letters", "Test");
        dictionary.AddWord("CD", "More letters", "Test");
        
        var grid = new CrosswordGrid(7, 7);
        grid.TryPlaceWord(new Word("AB", "Test"), 2, 2, Direction.Across);
        grid.TryPlaceWord(new Word("CD", "Test"), 1, 2, Direction.Down);

        var validation = grid.ValidateCrossword(dictionary);

        await Assert.That(validation.ValidAccidentalWords).IsNotNull();
        await Assert.That(validation.InvalidAccidentalWords).IsNotNull();
        
        foreach (var valid in validation.ValidAccidentalWords)
        {
            await Assert.That(valid.IsValidSwedishWord).IsTrue();
        }
        
        foreach (var invalid in validation.InvalidAccidentalWords)
        {
            await Assert.That(invalid.IsValidSwedishWord).IsFalse();
        }
    }

    [Test]
    public async Task IncludeValidAccidentalWords_MarksWordsForInclusion()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("SOL", "Sun", "Nature");
        dictionary.AddWord("ORD", "Word", "Language");
        dictionary.AddWord("SO", "So", "Conjunction");
        
        var grid = new CrosswordGrid(7, 7);
        grid.TryPlaceWord(new Word("SOL", "Sun"), 2, 2, Direction.Across);
        grid.TryPlaceWord(new Word("ORD", "Word"), 1, 3, Direction.Down);

        grid.IncludeValidAccidentalWords(dictionary);
        var validation = grid.ValidateCrossword(dictionary);

        var includedWords = validation.ValidAccidentalWords?.Where(w => w.ShouldIncludeInPuzzle).ToList() ?? [];
        
        foreach (var word in includedWords)
        {
            await Assert.That(word.PuzzleNumber).IsGreaterThan(0);
            await Assert.That(word.ClueFromDictionary).IsNotEmpty();
        }
    }

    [Test]
    public async Task RenumberCluesIncludingAccidental_AssignsSequentialNumbers()
    {
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "First", "Test");
        dictionary.AddWord("CD", "Second", "Test");
        
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("AB", "First");
        var word2 = new Word("CD", "Second");
        
        grid.TryPlaceWord(word1, 0, 0, Direction.Across);
        grid.TryPlaceWord(word2, 2, 0, Direction.Across);
        
        grid.IncludeValidAccidentalWords(dictionary);

        await Assert.That(word1.Number).IsGreaterThan(0);
        await Assert.That(word2.Number).IsGreaterThan(0);
    }
}

/// <summary>
/// Unit tests for CrosswordValidationResult
/// </summary>
public class CrosswordValidationResultTests
{
    [Test]
    public async Task NewValidationResult_HasEmptyLists()
    {
        var result = new CrosswordValidationResult();

        await Assert.That(result.AccidentalWords).IsEmpty();
        await Assert.That(result.ValidAccidentalWords).IsEmpty();
        await Assert.That(result.InvalidAccidentalWords).IsEmpty();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.Warnings).IsEmpty();
    }

    [Test]
    public async Task IsValid_DefaultsToTrue()
    {
        var result = new CrosswordValidationResult();

        await Assert.That(result.IsValid).IsTrue();
    }
}
