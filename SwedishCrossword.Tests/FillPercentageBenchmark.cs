using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;
using System.Diagnostics;

namespace SwedishCrossword.Tests;

/// <summary>
/// Benchmark test to verify fill percentage improvements
/// </summary>
public class FillPercentageBenchmark
{
    [Test]
    public async Task Benchmark_Easy_Crossword_Fill_Percentage()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        var validator = new GridValidator();
        var generator = new CrosswordGenerator(dictionary, validator);
        var options = CrosswordGenerationOptions.Easy;
        
        Console.WriteLine($"Dictionary has {dictionary.WordCount} words");
        Console.WriteLine($"Generating Easy crossword ({options.Width}x{options.Height})...");
        
        // Skip if no words
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary. Run Lexin import first.");
            return;
        }
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        var puzzle = await generator.GenerateAsync(options);
        stopwatch.Stop();
        
        // Assert & Report
        var stats = puzzle.Statistics;
        Console.WriteLine();
        Console.WriteLine("=== BENCHMARK RESULTS ===");
        Console.WriteLine($"Grid Size: {options.Width}x{options.Height}");
        Console.WriteLine($"Fill Percentage: {stats.FillPercentage:F1}%");
        Console.WriteLine($"Words Placed: {stats.WordCount}");
        Console.WriteLine($"Filled Cells: {stats.FilledCells}/{stats.TotalCells}");
        Console.WriteLine($"Generation Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Attempts: {puzzle.GenerationAttempts}");
        Console.WriteLine();
        
        // Verify we meet target
        await Assert.That(stats.FillPercentage).IsGreaterThanOrEqualTo(options.TargetFillPercentage);
    }
    
    [Test]
    public async Task Benchmark_Medium_Crossword_Fill_Percentage()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        var validator = new GridValidator();
        var generator = new CrosswordGenerator(dictionary, validator);
        var options = CrosswordGenerationOptions.Medium;
        
        Console.WriteLine($"Dictionary has {dictionary.WordCount} words");
        Console.WriteLine($"Generating Medium crossword ({options.Width}x{options.Height})...");
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        // Act
        var stopwatch = Stopwatch.StartNew();
        var puzzle = await generator.GenerateAsync(options);
        stopwatch.Stop();
        
        // Report
        var stats = puzzle.Statistics;
        Console.WriteLine();
        Console.WriteLine("=== BENCHMARK RESULTS ===");
        Console.WriteLine($"Grid Size: {options.Width}x{options.Height}");
        Console.WriteLine($"Fill Percentage: {stats.FillPercentage:F1}%");
        Console.WriteLine($"Words Placed: {stats.WordCount}");
        Console.WriteLine($"Filled Cells: {stats.FilledCells}/{stats.TotalCells}");
        Console.WriteLine($"Generation Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Attempts: {puzzle.GenerationAttempts}");
        
        await Assert.That(stats.FillPercentage).IsGreaterThanOrEqualTo(options.TargetFillPercentage);
    }
    
    [Test]
    public async Task Benchmark_Multiple_Easy_Crosswords_Average_Fill()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        var validator = new GridValidator();
        var generator = new CrosswordGenerator(dictionary, validator);
        var options = CrosswordGenerationOptions.Easy;
        
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("SKIPPED: No words in dictionary.");
            return;
        }
        
        const int iterations = 5;
        var fillPercentages = new List<double>();
        var wordCounts = new List<int>();
        var times = new List<long>();
        
        Console.WriteLine($"Generating {iterations} Easy crosswords...");
        
        // Act
        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var puzzle = await generator.GenerateAsync(options);
            stopwatch.Stop();
            
            fillPercentages.Add(puzzle.Statistics.FillPercentage);
            wordCounts.Add(puzzle.Statistics.WordCount);
            times.Add(stopwatch.ElapsedMilliseconds);
            
            Console.WriteLine($"  Run {i + 1}: {puzzle.Statistics.FillPercentage:F1}% fill, {puzzle.Statistics.WordCount} words, {stopwatch.ElapsedMilliseconds}ms");
        }
        
        // Report
        Console.WriteLine();
        Console.WriteLine("=== AGGREGATE RESULTS ===");
        Console.WriteLine($"Average Fill: {fillPercentages.Average():F1}%");
        Console.WriteLine($"Min Fill: {fillPercentages.Min():F1}%");
        Console.WriteLine($"Max Fill: {fillPercentages.Max():F1}%");
        Console.WriteLine($"Average Words: {wordCounts.Average():F1}");
        Console.WriteLine($"Average Time: {times.Average():F0}ms");
        
        // All should meet target
        await Assert.That(fillPercentages.Min()).IsGreaterThanOrEqualTo(options.TargetFillPercentage);
    }
}
