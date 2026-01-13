using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Services;
using SwedishCrossword.Models;
using System.Text;

namespace SwedishCrossword.Tests;

public class SwedishCharacterTests
{
    [Test]
    public async Task LoadAllWords_ShouldContainSwedishCharacters()
    {
        // Arrange & Act
        var dictionary = new SwedishDictionary();
        var allWords = dictionary.AllWords;
        
        // Assert
        await Assert.That(allWords.Count).IsGreaterThan(1000);
        
        // Verify specific Swedish characters are present
        var wordsWithÅ = allWords.Where(w => w.Text.Contains('Å')).ToList();
        var wordsWithÄ = allWords.Where(w => w.Text.Contains('Ä')).ToList();
        var wordsWithÖ = allWords.Where(w => w.Text.Contains('Ö')).ToList();
        
        await Assert.That(wordsWithÅ.Count).IsGreaterThan(0);
        await Assert.That(wordsWithÄ.Count).IsGreaterThan(0);
        await Assert.That(wordsWithÖ.Count).IsGreaterThan(0);
        
        Console.WriteLine($"Total words loaded: {allWords.Count}");
        Console.WriteLine($"Words with Å: {wordsWithÅ.Count}");
        Console.WriteLine($"Words with Ä: {wordsWithÄ.Count}");
        Console.WriteLine($"Words with Ö: {wordsWithÖ.Count}");
        
        // Sample some words with Swedish characters
        Console.WriteLine("\nSample words with Å:");
        foreach (var word in wordsWithÅ.Take(5))
        {
            Console.WriteLine($"  {word.Text} - {word.Clue}");
        }
        
        Console.WriteLine("\nSample words with Ä:");
        foreach (var word in wordsWithÄ.Take(5))
        {
            Console.WriteLine($"  {word.Text} - {word.Clue}");
        }
        
        Console.WriteLine("\nSample words with Ö:");
        foreach (var word in wordsWithÖ.Take(5))
        {
            Console.WriteLine($"  {word.Text} - {word.Clue}");
        }
    }
    
    [Test]
    public async Task AllWordFiles_ShouldLoadWithoutEncodingErrors()
    {
        // Arrange & Act
        var dictionary = new SwedishDictionary();
        var stats = dictionary.GetStatistics();
        
        // Assert
        await Assert.That(stats.TotalWords).IsGreaterThan(1000);
        
        Console.WriteLine($"?? Total words loaded: {stats.TotalWords}");
        Console.WriteLine($"?? Average word length: {stats.AverageLength:F1}");
        Console.WriteLine($"?? Length range: {stats.MinLength} - {stats.MaxLength}");
        
        Console.WriteLine("\n?? Top categories:");
        foreach (var category in stats.Categories.OrderByDescending(c => c.Value).Take(10))
        {
            Console.WriteLine($"  {category.Key}: {category.Value} words");
        }
        
        Console.WriteLine("\n?? Difficulty distribution:");
        foreach (var diff in stats.DifficultyDistribution.OrderByDescending(d => d.Value))
        {
            Console.WriteLine($"  {diff.Key}: {diff.Value} words");
        }
        
        // Verify we have a good distribution of Swedish characters
        var allWords = dictionary.AllWords;
        var swedishCharCount = allWords.Count(w => 
            w.Text.Contains('Å') || w.Text.Contains('Ä') || w.Text.Contains('Ö') ||
            w.Clue.Contains('å') || w.Clue.Contains('ä') || w.Clue.Contains('ö'));
            
        Console.WriteLine($"\n???? Words containing Swedish characters: {swedishCharCount}");
        Console.WriteLine($"???? Percentage with Swedish chars: {(swedishCharCount * 100.0 / allWords.Count):F1}%");
        
        await Assert.That(swedishCharCount).IsGreaterThan(0);
        
        // Test some specific Swedish character combinations
        var åWords = allWords.Where(w => w.Text.Contains('Å') || w.Clue.Contains('å')).ToList();
        var äWords = allWords.Where(w => w.Text.Contains('Ä') || w.Clue.Contains('ä')).ToList();
        var öWords = allWords.Where(w => w.Text.Contains('Ö') || w.Clue.Contains('ö')).ToList();
        
        Console.WriteLine($"\n?? Character distribution:");
        Console.WriteLine($"  Å/å: {åWords.Count} words");
        Console.WriteLine($"  Ä/ä: {äWords.Count} words");
        Console.WriteLine($"  Ö/ö: {öWords.Count} words");
        
        await Assert.That(åWords.Count).IsGreaterThan(0);
        await Assert.That(äWords.Count).IsGreaterThan(0);
        await Assert.That(öWords.Count).IsGreaterThan(0);
    }
    
    [Test]
    public async Task SwedishCharacterEncoding_ShouldBeConsistent()
    {
        // Arrange
        var dictionary = new SwedishDictionary();
        var allWords = dictionary.AllWords;
        
        // Test specific words with known Swedish characters
        var testWords = new Dictionary<string, string[]>
        {
            ["Å"] = ["JORDGUBBAR", "ÅLDERN", "KÅLROT"],
            ["Ä"] = ["ÄPPLE", "TRÄD", "BJÖRN"],
            ["Ö"] = ["DÖRR", "FÖREMÅL", "KÖTT"]
        };
        
        foreach (var charTest in testWords)
        {
            var character = charTest.Key;
            var expectedWords = charTest.Value;
            
            Console.WriteLine($"\nTesting character {character}:");
            
            foreach (var expectedWord in expectedWords)
            {
                var foundWord = allWords.FirstOrDefault(w => 
                    w.Text.Equals(expectedWord, StringComparison.OrdinalIgnoreCase));
                    
                if (foundWord != null)
                {
                    Console.WriteLine($"  ? Found: {foundWord.Text} - {foundWord.Clue}");
                    await Assert.That(foundWord.Text).Contains(character);
                }
                else
                {
                    // Find similar words for debugging
                    var similarWords = allWords.Where(w => 
                        w.Text.Contains(character) && 
                        w.Text.StartsWith(expectedWord.Substring(0, Math.Min(3, expectedWord.Length))))
                        .Take(3).ToList();
                        
                    Console.WriteLine($"  ? Not found: {expectedWord}");
                    if (similarWords.Any())
                    {
                        Console.WriteLine($"    Similar words found:");
                        foreach (var similar in similarWords)
                        {
                            Console.WriteLine($"      {similar.Text} - {similar.Clue}");
                        }
                    }
                }
            }
        }
        
        // Test that Swedish characters in clues are also properly encoded
        var cluesWithSwedishChars = allWords
            .Where(w => w.Clue.Contains('å') || w.Clue.Contains('ä') || w.Clue.Contains('ö'))
            .Take(10)
            .ToList();
            
        Console.WriteLine($"\nSample clues with Swedish characters:");
        foreach (var word in cluesWithSwedishChars)
        {
            Console.WriteLine($"  {word.Text}: {word.Clue}");
        }
        
        await Assert.That(cluesWithSwedishChars.Count).IsGreaterThan(0);
    }
}