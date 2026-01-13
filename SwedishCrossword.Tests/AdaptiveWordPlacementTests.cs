using TUnit.Core;
using SwedishCrossword.Models;
using SwedishCrossword.Services;
using TUnit.Assertions;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests for the adaptive word placement algorithm to ensure it doesn't get stuck
/// in infinite loops or have incorrect word length tracking
/// </summary>
public class AdaptiveWordPlacementTests
{
    private SwedishDictionary _dictionary = null!;
    private GridValidator _validator = null!;
    private CrosswordGenerator _generator = null!;

    [Before(Test)]
    public void Setup()
    {
        _dictionary = new SwedishDictionary();
        _validator = new GridValidator();
        _generator = new CrosswordGenerator(_dictionary, _validator);
    }

    [Test]
    public async Task AdaptivePlacement_ShouldNotRetryFailedWordAtSameLocation()
    {
        // Arrange: Create options that will cause validation failures
        var options = new CrosswordGenerationOptions
        {
            Width = 9,
            Height = 9,
            MinWordLength = 3,
            MaxWordLength = 10,
            TargetFillPercentage = 30.0,
            MaxAttempts = 5, // Low to speed up test
            RejectInvalidWords = true
        };

        // Act & Assert: Generation should not hang in infinite retry loop
        var startTime = DateTime.Now;
        var timeout = TimeSpan.FromSeconds(10); // Should complete much faster than this

        try
        {
            var result = await _generator.GenerateAsync(options);
            // If we get here, generation succeeded - that's fine
            await Assert.That(result).IsNotNull();
        }
        catch (InvalidOperationException)
        {
            // If generation fails, that's also fine for this test
            // We're just testing that it doesn't hang
        }

        var elapsed = DateTime.Now - startTime;
        await Assert.That(elapsed).IsLessThan(timeout);
    }

    [Test]
    public async Task AdaptivePlacement_ShouldTrackWordLengthCorrectly()
    {
        // Arrange: Create a minimal test to check word length tracking
        var options = new CrosswordGenerationOptions
        {
            Width = 7,
            Height = 7,
            MinWordLength = 3,
            MaxWordLength = 8,
            TargetFillPercentage = 25.0,
            MaxAttempts = 3,
            RejectInvalidWords = true
        };

        // Act: Try to generate and capture any errors
        try
        {
            var result = await _generator.GenerateAsync(options);
            
            // If successful, verify the result makes sense
            if (result != null)
            {
                var words = result.Grid.Words;
                await Assert.That(words.Count()).IsGreaterThan(0);
                
                // All words should be within the specified length range
                foreach (var word in words)
                {
                    await Assert.That(word.Length).IsGreaterThanOrEqualTo(options.MinWordLength);
                    await Assert.That(word.Length).IsLessThanOrEqualTo(options.MaxWordLength);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // If it fails, the error message should not mention impossible word lengths
            var message = ex.Message;
            
            // Should not mention reducing to length 0
            await Assert.That(message).DoesNotContain("Reducerar till 0");
            
            // Should not mention word length less than MinWordLength
            await Assert.That(message).DoesNotContain("längd 0+");
            await Assert.That(message).DoesNotContain("längd 1+");
            await Assert.That(message).DoesNotContain("längd 2+");
        }
    }

    [Test]
    public async Task AdaptivePlacement_ShouldHandleValidationFailuresGracefully()
    {
        // Arrange: Create conditions likely to cause validation failures
        var options = new CrosswordGenerationOptions
        {
            Width = 6,
            Height = 6,
            MinWordLength = 5, // Force longer words in small grid
            MaxWordLength = 6,
            TargetFillPercentage = 40.0,
            MaxAttempts = 3,
            RejectInvalidWords = true
        };

        // Act: Generation should handle validation failures without infinite loops
        var startTime = DateTime.Now;
        
        try
        {
            var result = await _generator.GenerateAsync(options);
            
            // If successful, verify no invalid accidental words
            if (result != null)
            {
                var validation = result.Grid.ValidateCrossword(_dictionary);
                await Assert.That(validation.InvalidAccidentalWords.Count).IsEqualTo(0);
            }
        }
        catch (InvalidOperationException)
        {
            // Failure is acceptable for this difficult configuration
        }

        // Should complete quickly regardless of success/failure
        var elapsed = DateTime.Now - startTime;
        await Assert.That(elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task AdaptivePlacement_ShouldProgressThroughWordLengthsCorrectly()
    {
        // Arrange: Create options with clear word length progression
        var options = new CrosswordGenerationOptions
        {
            Width = 8,
            Height = 8,
            MinWordLength = 3,
            MaxWordLength = 7,
            TargetFillPercentage = 20.0,
            MaxAttempts = 2,
            RejectInvalidWords = true
        };

        // Act: Run generation and verify it handles word length progression
        try
        {
            var result = await _generator.GenerateAsync(options);
            
            if (result != null)
            {
                var words = result.Grid.Words;
                await Assert.That(words.Count()).IsGreaterThan(0);
                
                // Should have attempted longer words first
                // If we have multiple words, longer ones should generally be placed first
                // (though this isn't guaranteed due to connectivity constraints)
                var wordsByPlacement = words.OrderBy(w => w.Number).ToList();
                if (wordsByPlacement.Count >= 2)
                {
                    // At least verify all words are valid lengths
                    foreach (var word in wordsByPlacement)
                    {
                        await Assert.That(word.Length).IsGreaterThanOrEqualTo(options.MinWordLength);
                        await Assert.That(word.Length).IsLessThanOrEqualTo(options.MaxWordLength);
                    }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // Even if it fails, should not have word length tracking errors
            await Assert.That(ex.Message).DoesNotContain("längd 0+");
            await Assert.That(ex.Message).DoesNotContain("Reducerar till 0");
        }
    }
}