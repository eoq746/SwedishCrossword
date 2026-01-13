using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for GridCell functionality focusing on state management and behavior
/// </summary>
public class GridCellBehaviorTests
{
    [Test]
    public async Task Empty_cell_has_correct_default_state()
    {
        // Arrange & Act
        var cell = new GridCell();

        // Assert
        await Assert.That(cell.IsEmpty).IsTrue();
        await Assert.That(cell.HasLetter).IsFalse();
        await Assert.That(cell.IsBlocked).IsFalse();
        await Assert.That(cell.IsPartOfWord).IsFalse();
        await Assert.That(cell.IsNumbered).IsFalse();
    }

    [Test]
    public async Task Setting_letter_updates_cell_properties()
    {
        // Arrange
        var cell = new GridCell();
        const string wordId = "word-123";

        // Act
        cell.SetLetter('k', wordId);

        // Assert
        await Assert.That(cell.Letter).IsEqualTo('K'); // Should be uppercase
        await Assert.That(cell.HasLetter).IsTrue();
        await Assert.That(cell.IsPartOfWord).IsTrue();
        await Assert.That(cell.WordIds).Contains(wordId);
        await Assert.That(cell.IsEmpty).IsFalse();
    }

    [Test]
    public async Task Setting_letter_handles_swedish_characters()
    {
        // Arrange
        var cell = new GridCell();

        // Act
        cell.SetLetter('å', "test-word");

        // Assert
        await Assert.That(cell.Letter).IsEqualTo('Å');
    }

    [Test]
    public async Task Blocking_cell_clears_all_content()
    {
        // Arrange
        var cell = new GridCell();
        cell.SetLetter('A', "word1");
        cell.Number = 5;

        // Act
        cell.Block();

        // Assert
        await Assert.That(cell.IsBlocked).IsTrue();
        await Assert.That(cell.HasLetter).IsFalse();
        await Assert.That(cell.IsPartOfWord).IsFalse();
        await Assert.That(cell.Number).IsEqualTo(0);
        await Assert.That(cell.WordIds).IsEmpty();
    }

    [Test]
    public async Task Cell_can_belong_to_multiple_words()
    {
        // Arrange
        var cell = new GridCell();

        // Act
        cell.SetLetter('A', "horizontal-word");
        cell.SetLetter('A', "vertical-word"); // Same letter, different word

        // Assert
        await Assert.That(cell.WordIds.Count).IsEqualTo(2);
        await Assert.That(cell.WordIds).Contains("horizontal-word");
        await Assert.That(cell.WordIds).Contains("vertical-word");
    }

    [Test]
    public async Task ToString_returns_correct_representation()
    {
        // Test empty cell
        var emptyCell = new GridCell();
        await Assert.That(emptyCell.ToString()).IsEqualTo(" ");

        // Test cell with letter
        var letterCell = new GridCell();
        letterCell.SetLetter('K', "word");
        await Assert.That(letterCell.ToString()).IsEqualTo("K");

        // Test blocked cell  
        var blockedCell = new GridCell();
        blockedCell.Block();
        await Assert.That(blockedCell.ToString()).IsEqualTo("#"); // Changed to expect hash instead of Unicode character
    }
}

/// <summary>
/// Tests for Word model focusing on construction and position calculations
/// </summary>
public class WordModelTests
{
    [Test]
    public async Task Constructor_normalizes_text_to_uppercase()
    {
        // Arrange & Act
        var word = new Word("katt", "En husdjur", "Djur", DifficultyLevel.Easy);

        // Assert
        await Assert.That(word.Text).IsEqualTo("KATT");
        await Assert.That(word.Clue).IsEqualTo("En husdjur");
        await Assert.That(word.Category).IsEqualTo("Djur");
        await Assert.That(word.Difficulty).IsEqualTo(DifficultyLevel.Easy);
        await Assert.That(word.Length).IsEqualTo(4);
    }

    [Test]
    public async Task New_word_is_not_placed()
    {
        // Arrange & Act
        var word = new Word("TEST", "Test clue");

        // Assert
        await Assert.That(word.IsPlaced).IsFalse();
        await Assert.That(word.Number).IsEqualTo(0);
        await Assert.That(word.GetPositions()).IsEmpty();
    }

    [Test]
    public async Task GetCharAt_returns_correct_character()
    {
        // Arrange
        var word = new Word("KATT", "Test");

        // Act & Assert
        await Assert.That(word.GetCharAt(0)).IsEqualTo('K');
        await Assert.That(word.GetCharAt(3)).IsEqualTo('T');
    }

    [Test]
    public async Task GetCharAt_throws_for_invalid_position()
    {
        // Arrange
        var word = new Word("TEST", "Clue");

        // Act & Assert
        await Assert.That(() => word.GetCharAt(4))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => word.GetCharAt(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Placed_word_across_returns_correct_positions()
    {
        // Arrange
        var word = new Word("CAT", "Animal");
        word.StartRow = 2;
        word.StartColumn = 3;
        word.Direction = Direction.Across;
        word.IsPlaced = true;

        // Act
        var positions = word.GetPositions().ToList();

        // Assert
        await Assert.That(positions.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo((2, 3));
        await Assert.That(positions[1]).IsEqualTo((2, 4));
        await Assert.That(positions[2]).IsEqualTo((2, 5));
    }

    [Test]
    public async Task Placed_word_down_returns_correct_positions()
    {
        // Arrange
        var word = new Word("DOG", "Pet");
        word.StartRow = 1;
        word.StartColumn = 5;
        word.Direction = Direction.Down;
        word.IsPlaced = true;

        // Act
        var positions = word.GetPositions().ToList();

        // Assert
        await Assert.That(positions.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo((1, 5));
        await Assert.That(positions[1]).IsEqualTo((2, 5));
        await Assert.That(positions[2]).IsEqualTo((3, 5));
    }

    [Test]
    public async Task End_positions_calculated_correctly()
    {
        // Arrange
        var word = new Word("HELLO", "Greeting");
        word.StartRow = 3;
        word.StartColumn = 2;
        word.IsPlaced = true;

        // Test across direction
        word.Direction = Direction.Across;
        await Assert.That(word.EndRow).IsEqualTo(3);
        await Assert.That(word.EndColumn).IsEqualTo(6);

        // Test down direction
        word.Direction = Direction.Down;
        await Assert.That(word.EndRow).IsEqualTo(7);
        await Assert.That(word.EndColumn).IsEqualTo(2);
    }
}

/// <summary>
/// Tests for basic CrosswordGrid operations
/// </summary>
public class CrosswordGridTests
{
    [Test]
    public async Task Constructor_throws_for_invalid_dimensions()
    {
        // Assert
        await Assert.That(() => new CrosswordGrid(0, 5))
            .Throws<ArgumentException>();
        await Assert.That(() => new CrosswordGrid(5, 0))
            .Throws<ArgumentException>();
        await Assert.That(() => new CrosswordGrid(-1, 5))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_creates_grid_with_correct_dimensions()
    {
        // Arrange & Act
        var grid = new CrosswordGrid(8, 6);

        // Assert
        await Assert.That(grid.Width).IsEqualTo(8);
        await Assert.That(grid.Height).IsEqualTo(6);
        await Assert.That(grid.Words.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IsValidPosition_returns_correct_results()
    {
        // Arrange
        var grid = new CrosswordGrid(3, 3);

        // Assert - valid positions
        await Assert.That(grid.IsValidPosition(0, 0)).IsTrue();
        await Assert.That(grid.IsValidPosition(2, 2)).IsTrue();
        await Assert.That(grid.IsValidPosition(1, 1)).IsTrue();

        // Assert - invalid positions
        await Assert.That(grid.IsValidPosition(-1, 0)).IsFalse();
        await Assert.That(grid.IsValidPosition(0, -1)).IsFalse();
        await Assert.That(grid.IsValidPosition(3, 0)).IsFalse();
        await Assert.That(grid.IsValidPosition(0, 3)).IsFalse();
    }

    [Test]
    public async Task GetCell_returns_empty_cell_for_new_grid()
    {
        // Arrange
        var grid = new CrosswordGrid(5, 5);

        // Act
        var cell = grid.GetCell(2, 3);

        // Assert
        await Assert.That(cell).IsNotNull();
        await Assert.That(cell.IsEmpty).IsTrue();
    }

    [Test]
    public async Task GetCell_throws_for_out_of_bounds()
    {
        // Arrange
        var grid = new CrosswordGrid(3, 3);

        // Assert
        await Assert.That(() => grid.GetCell(3, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => grid.GetCell(0, 3))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TryPlaceWord_places_word_correctly()
    {
        // Arrange
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("DOG", "Pet");

        // Act
        var success = grid.TryPlaceWord(word, 3, 2, Direction.Across);

        // Assert
        await Assert.That(success).IsTrue();
        await Assert.That(word.IsPlaced).IsTrue();
        await Assert.That(word.StartRow).IsEqualTo(3);
        await Assert.That(word.StartColumn).IsEqualTo(2);
        await Assert.That(word.Direction).IsEqualTo(Direction.Across);
        await Assert.That(word.Number).IsGreaterThan(0);

        // Check grid cells contain the letters
        await Assert.That(grid.GetCell(3, 2).Letter).IsEqualTo('D');
        await Assert.That(grid.GetCell(3, 3).Letter).IsEqualTo('O');
        await Assert.That(grid.GetCell(3, 4).Letter).IsEqualTo('G');
    }

    [Test]
    public async Task TryPlaceWord_fails_when_out_of_bounds()
    {
        // Arrange
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        // Act
        var success = grid.TryPlaceWord(word, 0, 0, Direction.Across);

        // Assert
        await Assert.That(success).IsFalse();
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task GetStats_returns_correct_statistics()
    {
        // Arrange
        var grid = new CrosswordGrid(4, 4);
        var word = new Word("CAT", "Pet");
        grid.TryPlaceWord(word, 1, 1, Direction.Across);

        // Act
        var stats = grid.GetStats();

        // Assert
        await Assert.That(stats.TotalCells).IsEqualTo(16);
        await Assert.That(stats.FilledCells).IsEqualTo(3);
        await Assert.That(stats.BlockedCells).IsEqualTo(0);
        await Assert.That(stats.EmptyCells).IsEqualTo(13);
        await Assert.That(stats.WordCount).IsEqualTo(1);
        await Assert.That(stats.FillPercentage).IsEqualTo(18.75).Within(0.01);
    }
}

/// <summary>
/// Tests for Swedish dictionary functionality - focusing on behavior, not implementation
/// </summary>
public class SwedishDictionaryTests
{
    private static SwedishDictionary? _sharedDictionary;

    private SwedishDictionary GetDictionary()
    {
        return _sharedDictionary ??= new SwedishDictionary();
    }

    [Test]
    public async Task Dictionary_loads_without_errors()
    {
        // Arrange & Act - Just verify the dictionary can be constructed without throwing
        var dictionary = GetDictionary();

        // Assert - WordCount should be non-negative (could be 0 if no data files exist)
        await Assert.That(dictionary.WordCount).IsGreaterThanOrEqualTo(0);
        await Assert.That(dictionary.AllWords).IsNotNull();
    }

    [Test]
    public async Task Dictionary_contains_common_swedish_words_when_lexin_imported()
    {
        // Arrange
        var dictionary = GetDictionary();

        // Skip if dictionary is empty (Lexin not imported)
        if (dictionary.WordCount == 0)
            return;

        // Assert common Swedish words should be recognized after Lexin import
        await Assert.That(dictionary.IsValidWord("ABBORRE")).IsTrue();
        await Assert.That(dictionary.IsValidWord("ÖVNING")).IsTrue();
    }

    [Test]
    public async Task IsValidWord_returns_false_for_nonexistent_words()
    {
        // Arrange
        var dictionary = GetDictionary();

        // Assert - Non-words should never be recognized regardless of dictionary state
        await Assert.That(dictionary.IsValidWord("XYZABC123")).IsFalse();
        await Assert.That(dictionary.IsValidWord("")).IsFalse();
        await Assert.That(dictionary.IsValidWord("   ")).IsFalse();
    }

    [Test]
    public async Task GetWords_filters_by_length_correctly()
    {
        // Arrange
        var dictionary = new SwedishDictionary(empty: true);

        // Add some test words to ensure we have data
        dictionary.AddWord("AB", "Two letters", "Test");
        dictionary.AddWord("ABC", "Three letters", "Test");
        dictionary.AddWord("ABCDEFGH", "Eight letters", "Test");
        dictionary.AddWord("ABCDEFGHI", "Nine letters", "Test");

        // Act
        var shortWords = dictionary.GetWords(minLength: 2, maxLength: 3).ToList();
        var longWords = dictionary.GetWords(minLength: 8).ToList();

        // Assert
        await Assert.That(shortWords).IsNotEmpty();
        await Assert.That(shortWords.All(w => w.Length >= 2 && w.Length <= 3)).IsTrue();
        
        await Assert.That(longWords).IsNotEmpty();
        await Assert.That(longWords.All(w => w.Length >= 8)).IsTrue();
    }

    [Test]
    public async Task GetWordsWithLetter_finds_words_containing_letter()
    {
        // Arrange
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("KATT", "Cat", "Animals");
        dictionary.AddWord("HUND", "Dog", "Animals");
        dictionary.AddWord("FISK", "Fish", "Animals");

        // Act
        var wordsWithK = dictionary.GetWordsWithLetter('K').ToList();

        // Assert
        await Assert.That(wordsWithK).IsNotEmpty();
        await Assert.That(wordsWithK.All(w => w.Text.Contains('K'))).IsTrue();
        await Assert.That(wordsWithK.Count).IsEqualTo(2); // KATT and FISK
    }

    [Test]
    public async Task GetRandomWords_returns_requested_count()
    {
        // Arrange
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("ETT", "One", "Numbers");
        dictionary.AddWord("TVÅ", "Two", "Numbers");
        dictionary.AddWord("TRE", "Three", "Numbers");
        dictionary.AddWord("FYRA", "Four", "Numbers");
        dictionary.AddWord("FEM", "Five", "Numbers");

        // Act
        var randomWords = dictionary.GetRandomWords(3).ToList();

        // Assert
        await Assert.That(randomWords.Count).IsEqualTo(3);
        
        // Should not return duplicates
        await Assert.That(randomWords.Select(w => w.Text).Distinct().Count()).IsEqualTo(randomWords.Count);
    }

    [Test]
    public async Task GetRandomWords_returns_all_available_when_count_exceeds_available()
    {
        // Arrange
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("ETT", "One", "Numbers");
        dictionary.AddWord("TVÅ", "Two", "Numbers");

        // Act - request more than available
        var randomWords = dictionary.GetRandomWords(10).ToList();

        // Assert - should return only what's available
        await Assert.That(randomWords.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AddWord_increases_word_count()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        var initialCount = dictionary.WordCount;

        // Act
        dictionary.AddWord("TESTORD", "Ett testord", "Test", DifficultyLevel.Easy);

        // Assert
        await Assert.That(dictionary.WordCount).IsEqualTo(initialCount + 1);
        await Assert.That(dictionary.IsValidWord("TESTORD")).IsTrue();
    }

    [Test]
    public async Task AddWord_rejects_duplicate_words()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        dictionary.AddWord("UNIQUE123", "Test", "Test");

        // Act & Assert
        await Assert.That(() => dictionary.AddWord("unique123", "Duplicate", "Test"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddWord_rejects_empty_word_or_clue()
    {
        // Arrange
        var dictionary = new SwedishDictionary();

        // Act & Assert
        await Assert.That(() => dictionary.AddWord("", "Valid clue", "Test"))
            .Throws<ArgumentException>();
        await Assert.That(() => dictionary.AddWord("VALID", "", "Test"))
            .Throws<ArgumentException>();
        await Assert.That(() => dictionary.AddWord("   ", "Valid clue", "Test"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task GetStatistics_returns_valid_data_when_dictionary_has_words()
    {
        // Arrange
        var dictionary = new SwedishDictionary(empty: true);
        dictionary.AddWord("AB", "Short", "Test", DifficultyLevel.Easy);
        dictionary.AddWord("ABC", "Medium", "Test", DifficultyLevel.Medium);
        dictionary.AddWord("ABCD", "Longer", "Other", DifficultyLevel.Hard);

        // Act
        var stats = dictionary.GetStatistics();

        // Assert
        await Assert.That(stats.TotalWords).IsEqualTo(3);
        await Assert.That(stats.Categories.Count).IsEqualTo(2); // "Test" and "Other"
        await Assert.That(stats.LengthDistribution.Count).IsEqualTo(3); // lengths 2, 3, 4
        await Assert.That(stats.AverageLength).IsEqualTo(3.0);
        await Assert.That(stats.MinLength).IsEqualTo(2);
        await Assert.That(stats.MaxLength).IsEqualTo(4);
    }

    [Test]
    public async Task IsValidWord_is_case_insensitive()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        dictionary.AddWord("TESTORD", "Test", "Test");

        // Assert - all case variants should be recognized
        await Assert.That(dictionary.IsValidWord("TESTORD")).IsTrue();
        await Assert.That(dictionary.IsValidWord("testord")).IsTrue();
        await Assert.That(dictionary.IsValidWord("Testord")).IsTrue();
        await Assert.That(dictionary.IsValidWord("TeStOrD")).IsTrue();
    }

    [Test]
    public async Task CreateWord_returns_valid_word_instance()
    {
        // Arrange
        var dictionary = new SwedishDictionary();

        // Act
        var word = dictionary.CreateWord("TEST", "A test", "Category", DifficultyLevel.Hard);

        // Assert
        await Assert.That(word.Text).IsEqualTo("TEST");
        await Assert.That(word.Clue).IsEqualTo("A test");
        await Assert.That(word.Category).IsEqualTo("Category");
        await Assert.That(word.Difficulty).IsEqualTo(DifficultyLevel.Hard);
    }
}