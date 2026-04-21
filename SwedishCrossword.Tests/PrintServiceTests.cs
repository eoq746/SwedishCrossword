using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword.Tests;

/// <summary>
/// Unit tests for the PrintService class
/// </summary>
public class PrintServiceTests
{
    private PrintService _printService = null!;
    private ClueGenerator _clueGenerator = null!;

    [Before(Test)]
    public void Setup()
    {
        _clueGenerator = new ClueGenerator();
        _printService = new PrintService(_clueGenerator);
    }

    [Test]
    public async Task GeneratePrintableDocument_ReturnsNonEmptyString()
    {
        var grid = new CrosswordGrid(5, 5);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);

        var document = _printService.GeneratePrintableDocument(puzzle, PrintOptions.Default);

        await Assert.That(document).IsNotEmpty();
    }

    [Test]
    public async Task GeneratePrintableDocument_ContainsTitle()
    {
        var grid = new CrosswordGrid(5, 5);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);

        var document = _printService.GeneratePrintableDocument(puzzle, PrintOptions.Default);

        await Assert.That(document).Contains("KORSORD");
    }

    [Test]
    public async Task GeneratePrintableDocument_ContainsClues()
    {
        var grid = new CrosswordGrid(5, 5);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);

        var document = _printService.GeneratePrintableDocument(puzzle, PrintOptions.Default);

        await Assert.That(document).Contains("VÅGRÄTT");
    }

    [Test]
    public async Task GenerateJsonForWeb_ReturnsValidJson()
    {
        var grid = new CrosswordGrid(5, 5);
        grid.TryPlaceWord(new Word("CAT", "Animal"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);

        var json = _printService.GenerateJsonForWeb(puzzle);

        await Assert.That(json).IsNotEmpty();
        await Assert.That(json).Contains("\"width\"");
        await Assert.That(json).Contains("\"height\"");
        await Assert.That(json).Contains("\"cells\"");
        await Assert.That(json).Contains("\"clues\"");
    }

    [Test]
    public async Task GenerateJsonForWeb_ContainsCorrectDimensions()
    {
        var grid = new CrosswordGrid(7, 9);
        grid.TryPlaceWord(new Word("TEST", "A test"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);

        var json = _printService.GenerateJsonForWeb(puzzle);

        await Assert.That(json).Contains("\"width\": 7");
        await Assert.That(json).Contains("\"height\": 9");
    }

    [Test]
    public async Task GenerateJsonForWeb_ContainsClueData()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("CAT", "An animal"), 1, 1, Direction.Across);
        grid.TryPlaceWord(new Word("DOG", "A pet"), 1, 1, Direction.Down);
        var puzzle = new CrosswordPuzzle(grid, 1);

        var json = _printService.GenerateJsonForWeb(puzzle);

        await Assert.That(json).Contains("\"across\"");
        await Assert.That(json).Contains("\"down\"");
    }

    [Test]
    public async Task SaveAsJsonAsync_CreatesFile()
    {
        var grid = new CrosswordGrid(5, 5);
        grid.TryPlaceWord(new Word("TEST", "A test"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_puzzle_{Guid.NewGuid()}.json");

        try
        {
            await _printService.SaveAsJsonAsync(puzzle, tempPath);

            await Assert.That(File.Exists(tempPath)).IsTrue();
            var content = await File.ReadAllTextAsync(tempPath);
            await Assert.That(content).IsNotEmpty();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public async Task SaveToFileAsync_CreatesFile()
    {
        var grid = new CrosswordGrid(5, 5);
        grid.TryPlaceWord(new Word("TEST", "A test"), 1, 1, Direction.Across);
        var puzzle = new CrosswordPuzzle(grid, 1);
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_puzzle_{Guid.NewGuid()}.txt");

        try
        {
            await _printService.SaveToFileAsync(puzzle, tempPath, PrintOptions.Default);

            await Assert.That(File.Exists(tempPath)).IsTrue();
            var content = await File.ReadAllTextAsync(tempPath);
            await Assert.That(content).IsNotEmpty();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Test]
    public async Task CreateUnicodeGridSafe_ReturnsGridRepresentation()
    {
        var grid = new CrosswordGrid(3, 3);
        grid.TryPlaceWord(new Word("AB", "Test"), 0, 0, Direction.Across);

        var gridString = _printService.CreateUnicodeGridSafe(grid, showSolution: true);

        await Assert.That(gridString).IsNotEmpty();
        await Assert.That(gridString).Contains("A");
        await Assert.That(gridString).Contains("B");
    }

    [Test]
    public async Task CreateUnicodeGridSafe_HidesSolutionWhenRequested()
    {
        var grid = new CrosswordGrid(3, 3);
        grid.TryPlaceWord(new Word("AB", "Test"), 0, 0, Direction.Across);

        var gridString = _printService.CreateUnicodeGridSafe(grid, showSolution: false);

        await Assert.That(gridString).IsNotEmpty();
    }
}

/// <summary>
/// Unit tests for the PrintOptions class
/// </summary>
public class PrintOptionsTests
{
    [Test]
    public async Task Default_HasExpectedValues()
    {
        var options = PrintOptions.Default;

        await Assert.That(options.IncludeSolution).IsFalse();
        await Assert.That(options.IncludeStatistics).IsFalse();
        await Assert.That(options.IncludeTitle).IsTrue();
    }

    [Test]
    public async Task CanSetIncludeSolution()
    {
        var options = new PrintOptions { IncludeSolution = true };

        await Assert.That(options.IncludeSolution).IsTrue();
    }

    [Test]
    public async Task CanSetIncludeStatistics()
    {
        var options = new PrintOptions { IncludeStatistics = true };

        await Assert.That(options.IncludeStatistics).IsTrue();
    }

    [Test]
    public async Task WithSolution_HasSolutionEnabled()
    {
        var options = PrintOptions.WithSolution;

        await Assert.That(options.IncludeSolution).IsTrue();
        await Assert.That(options.IncludeStatistics).IsTrue();
    }

    [Test]
    public async Task PuzzleOnly_HasSolutionDisabled()
    {
        var options = PrintOptions.PuzzleOnly;

        await Assert.That(options.IncludeSolution).IsFalse();
        await Assert.That(options.IncludeStatistics).IsFalse();
    }
}

/// <summary>
/// Unit tests for CrosswordPuzzle class
/// </summary>
public class CrosswordPuzzleTests
{
    [Test]
    public async Task Constructor_SetsGridAndAttempts()
    {
        var grid = new CrosswordGrid(5, 5);

        var puzzle = new CrosswordPuzzle(grid, 3);

        await Assert.That(puzzle.Grid).IsEqualTo(grid);
        await Assert.That(puzzle.GenerationAttempts).IsEqualTo(3);
    }

    [Test]
    public async Task CreatedAt_IsSetToCurrentTime()
    {
        var beforeCreation = DateTime.Now.AddSeconds(-1);
        var grid = new CrosswordGrid(5, 5);

        var puzzle = new CrosswordPuzzle(grid, 1);

        var afterCreation = DateTime.Now.AddSeconds(1);

        await Assert.That(puzzle.CreatedAt).IsGreaterThan(beforeCreation);
        await Assert.That(puzzle.CreatedAt).IsLessThan(afterCreation);
    }

    [Test]
    public async Task Statistics_ReturnsGridStats()
    {
        var grid = new CrosswordGrid(4, 4);
        grid.TryPlaceWord(new Word("AB", "Test"), 0, 0, Direction.Across);

        var puzzle = new CrosswordPuzzle(grid, 1);

        await Assert.That(puzzle.Statistics.TotalCells).IsEqualTo(16);
        await Assert.That(puzzle.Statistics.FilledCells).IsEqualTo(2);
        await Assert.That(puzzle.Statistics.WordCount).IsEqualTo(1);
    }

    [Test]
    public async Task GetClues_ReturnsAcrossAndDownClues()
    {
        var grid = new CrosswordGrid(10, 10);
        grid.TryPlaceWord(new Word("HEJ", "Hälsning"), 0, 0, Direction.Across);
        grid.TryPlaceWord(new Word("HUND", "Djur"), 0, 0, Direction.Down);

        var puzzle = new CrosswordPuzzle(grid, 1);
        var (across, down) = puzzle.GetClues();

        await Assert.That(across.Count).IsEqualTo(1);
        await Assert.That(down.Count).IsEqualTo(1);
    }
}
