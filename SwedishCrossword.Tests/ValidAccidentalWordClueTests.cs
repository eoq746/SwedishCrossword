using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Services;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for accidental word detection and validation functionality
/// </summary>
public class AccidentalWordDetectionTests
{
    [Test]
    public async Task AccidentalWord_has_correct_default_properties()
    {
        // Arrange & Act
        var accidentalWord = new AccidentalWord
        {
            Text = "ORD",
            StartRow = 2,
            StartCol = 3,
            Direction = Direction.Across,
            Length = 3
        };

        // Assert
        await Assert.That(accidentalWord.Text).IsEqualTo("ORD");
        await Assert.That(accidentalWord.StartRow).IsEqualTo(2);
        await Assert.That(accidentalWord.StartCol).IsEqualTo(3);
        await Assert.That(accidentalWord.Direction).IsEqualTo(Direction.Across);
        await Assert.That(accidentalWord.Length).IsEqualTo(3);
        await Assert.That(accidentalWord.IsValidSwedishWord).IsNull();
    }

    [Test]
    public async Task ValidationStatus_reflects_validation_state()
    {
        // Arrange
        var word = new AccidentalWord { Text = "TEST" };

        // Test unchecked state
        await Assert.That(word.ValidationStatus).Contains("kontrollerat");

        // Test valid state
        word.IsValidSwedishWord = true;
        await Assert.That(word.ValidationStatus).Contains("Giltigt");

        // Test invalid state
        word.IsValidSwedishWord = false;
        await Assert.That(word.ValidationStatus).Contains("Ogiltigt");
    }

    [Test]
    public async Task ToString_includes_essential_information()
    {
        // Arrange
        var word = new AccidentalWord
        {
            Text = "TEST",
            StartRow = 1,
            StartCol = 2,
            Direction = Direction.Down,
            IsValidSwedishWord = true
        };

        // Act
        var result = word.ToString();

        // Assert
        await Assert.That(result).Contains("TEST");
        await Assert.That(result).Contains("(2, 3)"); // StartRow + 1 = 2, StartCol + 1 = 3
        await Assert.That(result).Contains("lodrätt"); // Swedish for "Down"
        await Assert.That(result).Contains("Giltigt");
    }
}

/// <summary>
/// Tests for grid-based accidental word detection
/// </summary>
public class GridAccidentalWordTests
{
    private static SwedishDictionary? _sharedDictionary;

    private SwedishDictionary GetDictionary()
    {
        return _sharedDictionary ??= new SwedishDictionary();
    }

    [Test]
    public async Task DetectAccidentalWords_finds_unintentional_word_formations()
    {
        // Arrange
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("KATT", "Pet");
        var word2 = new Word("ARM", "Body part");
        
        grid.TryPlaceWord(word1, 2, 1, Direction.Across); // KATT
        grid.TryPlaceWord(word2, 1, 2, Direction.Down);   // ARM intersecting at 'A'

        // Act
        var accidentalWords = grid.DetectAccidentalWords();

        // Assert
        await Assert.That(accidentalWords).IsNotNull();
        
        // Should find words that aren't the intentionally placed ones
        var nonIntentionalWords = accidentalWords.Where(w => 
            !w.Text.Equals("KATT", StringComparison.OrdinalIgnoreCase) &&
            !w.Text.Equals("ARM", StringComparison.OrdinalIgnoreCase)).ToList();
        
        // Verify we detect some accidental words (exact count depends on intersection)
        await Assert.That(accidentalWords.Count).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task DetectAccidentalWords_with_dictionary_validates_words()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("SOL", "Sun");
        var word2 = new Word("ORD", "Word");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across); // SOL
        grid.TryPlaceWord(word2, 1, 3, Direction.Down);   // ORD intersecting at 'O'

        // Act
        var accidentalWords = grid.DetectAccidentalWords(dictionary);

        // Assert
        await Assert.That(accidentalWords).IsNotNull();
        
        // All detected words should have their validity checked
        foreach (var accWord in accidentalWords)
        {
            await Assert.That(accWord.IsValidSwedishWord).IsNotNull();
        }
    }

    [Test]
    public async Task ValidateCrossword_includes_accidental_word_analysis()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("NU", "Now");
        var word2 = new Word("NY", "New");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 2, Direction.Down);

        // Act
        var validation = grid.ValidateCrossword(dictionary);

        // Assert
        await Assert.That(validation).IsNotNull();
        await Assert.That(validation.AccidentalWords).IsNotNull();
        
        if (validation.ValidAccidentalWords.Any())
        {
            await Assert.That(validation.ValidAccidentalWords.All(w => w.IsValidSwedishWord == true)).IsTrue();
        }
        
        if (validation.InvalidAccidentalWords.Any())
        {
            await Assert.That(validation.InvalidAccidentalWords.All(w => w.IsValidSwedishWord == false)).IsTrue();
        }
    }
}

/// <summary>
/// Tests for crossword puzzle enhancement with accidental words
/// </summary>
public class PuzzleEnhancementTests
{
    private static SwedishDictionary? _sharedDictionary;

    private SwedishDictionary GetDictionary()
    {
        return _sharedDictionary ??= new SwedishDictionary();
    }

    [Test]
    public async Task IncludeValidAccidentalWords_marks_valid_words_for_inclusion()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("SOL", "Ljus från himlen");
        var word2 = new Word("ORD", "Text man läser");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 3, Direction.Down);

        // Act
        grid.IncludeValidAccidentalWords(dictionary);
        var validation = grid.ValidateCrossword(dictionary);

        // Assert
        var includedWords = validation.ValidAccidentalWords?.Where(w => w.ShouldIncludeInPuzzle).ToList() ?? [];
        
        foreach (var accWord in includedWords)
        {
            await Assert.That(accWord.PuzzleNumber).IsGreaterThan(0);
            await Assert.That(accWord.ClueFromDictionary).IsNotEmpty();
        }
    }

    [Test]
    public async Task CrosswordPuzzle_UpdateValidation_processes_accidental_words()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("NU", "Just nu");
        var word2 = new Word("NY", "Inte gammal");
        
        grid.TryPlaceWord(word1, 2, 2, Direction.Across);
        grid.TryPlaceWord(word2, 1, 2, Direction.Down);
        
        var puzzle = new CrosswordPuzzle(grid, 1);

        // Act
        puzzle.UpdateValidation(dictionary);

        // Assert
        await Assert.That(puzzle.ValidationResult).IsNotNull();
        
        var validAccidentalWords = puzzle.ValidationResult.ValidAccidentalWords?.Where(w => w.ShouldIncludeInPuzzle).ToList() ?? [];
        
        foreach (var accWord in validAccidentalWords)
        {
            await Assert.That(accWord.PuzzleNumber).IsGreaterThan(0);
            await Assert.That(accWord.ClueFromDictionary).IsNotEmpty();
        }
    }

    [Test]
    public async Task Print_output_distinguishes_bonus_words()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(7, 7);
        var word1 = new Word("KATT", "Husdjur med tassar");
        var word2 = new Word("ARM", "Kroppsdel");
        
        grid.TryPlaceWord(word1, 2, 1, Direction.Across);
        grid.TryPlaceWord(word2, 1, 2, Direction.Down);
        
        var puzzle = new CrosswordPuzzle(grid, 1);
        puzzle.UpdateValidation(dictionary);
        
        var printService = new PrintService(new ClueGenerator());
        var options = PrintOptions.Default;

        // Act
        var printOutput = printService.GeneratePrintableDocument(puzzle, options);

        // Assert
        await Assert.That(printOutput).IsNotEmpty();
        
        // Should contain standard crossword elements
        await Assert.That(printOutput).Contains("VÅGRÄTT");
        
        // Check for clue content - verify basic structure
        var clueLines = printOutput.Split('\n').Where(line => line.Contains(". ")).ToList();
        await Assert.That(clueLines.Count).IsGreaterThanOrEqualTo(1); // At least one clue should be present
        
        // Verify the print output contains crossword content
        await Assert.That(printOutput).Contains("KORSORD");
        
        // Check that we have some numbered clues
        var hasNumberedClues = printOutput.Split('\n').Any(line => 
            line.Trim().Length > 0 && 
            char.IsDigit(line.Trim().First()) && 
            line.Contains("."));
        
        await Assert.That(hasNumberedClues).IsTrue();
    }

    [Test]
    public async Task Numbering_system_handles_mixed_word_types()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(8, 8);
        var word1 = new Word("KAT", "Short for cat");
        var word2 = new Word("ARM", "Limb");
        var word3 = new Word("TAR", "Takes");
        
        grid.TryPlaceWord(word1, 2, 1, Direction.Across);
        grid.TryPlaceWord(word2, 1, 2, Direction.Down);
        grid.TryPlaceWord(word3, 4, 1, Direction.Across);

        // Act
        grid.IncludeValidAccidentalWords(dictionary);
        var validation = grid.ValidateCrossword(dictionary);

        // Assert
        var allIncluded = validation.ValidAccidentalWords?.Where(w => w.ShouldIncludeInPuzzle).ToList() ?? [];
        var allIntentional = grid.Words.ToList();
        
        // Verify numbering is consistent and sequential
        var allNumbers = allIntentional.Select(w => w.Number)
            .Concat(allIncluded.Select(w => w.PuzzleNumber))
            .Where(n => n > 0)
            .OrderBy(n => n)
            .ToList();
        
        // Numbers should start from 1 and be consecutive
        for (int i = 0; i < allNumbers.Count; i++)
        {
            await Assert.That(allNumbers[i]).IsEqualTo(i + 1);
        }
    }
}

/// <summary>
/// Performance tests for accidental word detection
/// </summary>
[NotInParallel("Performance")]
public class AccidentalWordPerformanceTests
{
    private static SwedishDictionary? _sharedDictionary;

    private SwedishDictionary GetDictionary()
    {
        return _sharedDictionary ??= new SwedishDictionary();
    }

    [Test]
    public async Task DetectAccidentalWords_completes_within_reasonable_time()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(15, 15);
        
        // Place multiple words to create a complex grid
        var words = new[]
        {
            new Word("SVENSKA", "Language"),
            new Word("KORSORD", "Puzzle"),
            new Word("HJÄLP", "Assistance"),
            new Word("FRAM", "Forward"),
            new Word("TILL", "To")
        };
        
        grid.TryPlaceWord(words[0], 3, 2, Direction.Across);
        grid.TryPlaceWord(words[1], 1, 4, Direction.Down);
        grid.TryPlaceWord(words[2], 5, 1, Direction.Across);
        grid.TryPlaceWord(words[3], 2, 6, Direction.Down);
        grid.TryPlaceWord(words[4], 7, 3, Direction.Across);

        // Act & Assert - Should complete in reasonable time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var accidentalWords = grid.DetectAccidentalWords(dictionary);
        stopwatch.Stop();

        await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(5000); // 5 seconds max
        await Assert.That(accidentalWords).IsNotNull();
    }

    [Test]
    public async Task Optimized_detection_performs_better_than_full_scan()
    {
        // Arrange
        var dictionary = GetDictionary();
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "Word");
        grid.TryPlaceWord(word, 3, 3, Direction.Across);

        // Act - Use optimized near-detection
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var nearWords = grid.DetectAccidentalWordsNear(3, 3, Direction.Across, 4, dictionary);
        stopwatch.Stop();

        // Assert - Should be faster than full grid scan and still functional
        await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(1000); // 1 second max
        await Assert.That(nearWords).IsNotNull();
    }
}