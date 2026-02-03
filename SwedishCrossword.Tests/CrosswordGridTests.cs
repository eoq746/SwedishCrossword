using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the CrosswordGrid class
/// </summary>
public class CrosswordGridTests
{
    [Test]
    public async Task Constructor_CreatesGridWithCorrectDimensions()
    {
        var grid = new CrosswordGrid(8, 6);

        await Assert.That(grid.Width).IsEqualTo(8);
        await Assert.That(grid.Height).IsEqualTo(6);
        await Assert.That(grid.Words.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_ThrowsForZeroWidth()
    {
        await Assert.That(() => new CrosswordGrid(0, 5))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ThrowsForZeroHeight()
    {
        await Assert.That(() => new CrosswordGrid(5, 0))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ThrowsForNegativeWidth()
    {
        await Assert.That(() => new CrosswordGrid(-1, 5))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Constructor_ThrowsForNegativeHeight()
    {
        await Assert.That(() => new CrosswordGrid(5, -1))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task IsValidPosition_ReturnsTrueForValidPositions()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(0, 0)).IsTrue();
        await Assert.That(grid.IsValidPosition(2, 2)).IsTrue();
        await Assert.That(grid.IsValidPosition(1, 1)).IsTrue();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForNegativeRow()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(-1, 0)).IsFalse();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForNegativeColumn()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(0, -1)).IsFalse();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForRowOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(3, 0)).IsFalse();
    }

    [Test]
    public async Task IsValidPosition_ReturnsFalseForColumnOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(grid.IsValidPosition(0, 3)).IsFalse();
    }

    [Test]
    public async Task GetCell_ReturnsEmptyCell()
    {
        var grid = new CrosswordGrid(5, 5);

        var cell = grid.GetCell(2, 3);

        await Assert.That(cell).IsNotNull();
        await Assert.That(cell.IsEmpty).IsTrue();
    }

    [Test]
    public async Task GetCell_ThrowsForRowOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(() => grid.GetCell(3, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetCell_ThrowsForColumnOutOfBounds()
    {
        var grid = new CrosswordGrid(3, 3);

        await Assert.That(() => grid.GetCell(0, 3))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task TryPlaceWord_PlacesWordCorrectlyAcross()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("DOG", "Pet");

        var success = grid.TryPlaceWord(word, 3, 2, Direction.Across);

        await Assert.That(success).IsTrue();
        await Assert.That(word.IsPlaced).IsTrue();
        await Assert.That(word.StartRow).IsEqualTo(3);
        await Assert.That(word.StartColumn).IsEqualTo(2);
        await Assert.That(word.Direction).IsEqualTo(Direction.Across);
        await Assert.That(word.Number).IsGreaterThan(0);

        await Assert.That(grid.GetCell(3, 2).Letter).IsEqualTo('D');
        await Assert.That(grid.GetCell(3, 3).Letter).IsEqualTo('O');
        await Assert.That(grid.GetCell(3, 4).Letter).IsEqualTo('G');
    }

    [Test]
    public async Task TryPlaceWord_PlacesWordCorrectlyDown()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("CAT", "Animal");

        var success = grid.TryPlaceWord(word, 2, 4, Direction.Down);

        await Assert.That(success).IsTrue();
        await Assert.That(word.Direction).IsEqualTo(Direction.Down);

        await Assert.That(grid.GetCell(2, 4).Letter).IsEqualTo('C');
        await Assert.That(grid.GetCell(3, 4).Letter).IsEqualTo('A');
        await Assert.That(grid.GetCell(4, 4).Letter).IsEqualTo('T');
    }

    [Test]
    public async Task TryPlaceWord_FailsWhenWordExceedsGridWidth()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var success = grid.TryPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(success).IsFalse();
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task TryPlaceWord_FailsWhenWordExceedsGridHeight()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var success = grid.TryPlaceWord(word, 0, 0, Direction.Down);

        await Assert.That(success).IsFalse();
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task TryPlaceWord_AddsWordToCollection()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");

        grid.TryPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(grid.Words.Count).IsEqualTo(1);
        await Assert.That(grid.Words).Contains(word);
    }

    [Test]
    public async Task CanPlaceWord_ReturnsTrueForValidPlacement()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");

        var canPlace = grid.CanPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(canPlace).IsTrue();
    }

    [Test]
    public async Task CanPlaceWord_ReturnsFalseWhenOutOfBounds()
    {
        var grid = new CrosswordGrid(5, 5);
        var word = new Word("TOOLONG", "Too big");

        var canPlace = grid.CanPlaceWord(word, 0, 0, Direction.Across);

        await Assert.That(canPlace).IsFalse();
    }

    [Test]
    public async Task GetStats_ReturnsCorrectStatistics()
    {
        var grid = new CrosswordGrid(4, 4);
        var word = new Word("CAT", "Pet");
        grid.TryPlaceWord(word, 1, 1, Direction.Across);

        var stats = grid.GetStats();

        await Assert.That(stats.TotalCells).IsEqualTo(16);
        await Assert.That(stats.FilledCells).IsEqualTo(3);
        await Assert.That(stats.BlockedCells).IsEqualTo(0);
        await Assert.That(stats.EmptyCells).IsEqualTo(13);
        await Assert.That(stats.WordCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetStats_CalculatesFillPercentage()
    {
        var grid = new CrosswordGrid(4, 4);
        var word = new Word("CAT", "Pet");
        grid.TryPlaceWord(word, 1, 1, Direction.Across);

        var stats = grid.GetStats();

        await Assert.That(stats.FillPercentage).IsEqualTo(18.75).Within(0.01);
    }

    [Test]
    public async Task GetPlacedWordTexts_ReturnsAllPlacedWords()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("DOG", "Pet"), 2, 0, Direction.Across);

        var placedTexts = grid.GetPlacedWordTexts();

        await Assert.That(placedTexts.Count).IsEqualTo(2);
        await Assert.That(placedTexts).Contains("CAT");
        await Assert.That(placedTexts).Contains("DOG");
    }

    [Test]
    public async Task GetWordsByDirection_SeparatesAcrossAndDown()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("HEJ", "Hälsning"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("HUND", "Djur"), 0, 0, Direction.Down);

        var (across, down) = grid.GetWordsByDirection();

        await Assert.That(across.Count).IsEqualTo(1);
        await Assert.That(down.Count).IsEqualTo(1);
        await Assert.That(across[0].Text).IsEqualTo("HEJ");
        await Assert.That(down[0].Text).IsEqualTo("HUND");
    }

    [Test]
    public async Task RenumberClues_AssignsSequentialNumbers()
    {
        var grid = new CrosswordGrid(10, 10);
        var word1 = new Word("AB", "First");
        var word2 = new Word("CD", "Second");
        
        grid.TryPlaceWord(word1, 0, 0, Direction.Across);
        grid.TryPlaceWord(word2, 2, 0, Direction.Across);
        
        grid.RenumberClues();

        await Assert.That(word1.Number).IsEqualTo(1);
        await Assert.That(word2.Number).IsEqualTo(2);
    }

    [Test]
    public async Task RemoveWord_RemovesWordFromGrid()
    {
        var grid = new CrosswordGrid(10, 10);
        var word = new Word("TEST", "A test");
        grid.TryPlaceWord(word, 0, 0, Direction.Across);

        var removed = grid.RemoveWord(word);

        await Assert.That(removed).IsTrue();
        await Assert.That(grid.Words.Count).IsEqualTo(0);
        await Assert.That(word.IsPlaced).IsFalse();
    }

    [Test]
    public async Task FillEmptyCellsWithAsterisks_MarksEmptyCells()
    {
        var grid = new CrosswordGrid(3, 3);
        var word = new Word("AB", "Test");
        grid.TryPlaceWord(word, 0, 0, Direction.Across);

        grid.FillEmptyCellsWithAsterisks();

        await Assert.That(grid.GetCell(0, 0).Letter).IsEqualTo('A');
        await Assert.That(grid.GetCell(0, 1).Letter).IsEqualTo('B');
        await Assert.That(grid.GetCell(1, 0).Letter).IsEqualTo('*');
        await Assert.That(grid.GetCell(2, 2).Letter).IsEqualTo('*');
    }
}
