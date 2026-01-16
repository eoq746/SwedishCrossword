using System.Text.Json;
using TUnit.Assertions;
using TUnit.Core;
using SwedishCrossword.Services;
using SwedishCrossword.Models;

namespace SwedishCrossword.Tests;

/// <summary>
/// Tests to validate the integrity and correctness of the lexin-words.json data file.
/// These tests ensure that all word entries have valid structure and content.
/// </summary>
public class LexinWordsDataValidationTests
{
    private static List<WordEntry>? _cachedWordEntries;

    private static List<WordEntry> LoadWordEntries()
    {
        if (_cachedWordEntries != null)
        {
            return _cachedWordEntries;
        }

        var jsonPath = LexinWordImporter.GetJsonFilePath();
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"Lexin words file not found at: {jsonPath}");
        }

        var jsonText = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
        var entries = JsonSerializer.Deserialize<List<WordEntry>>(jsonText, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _cachedWordEntries = entries ?? [];
        return _cachedWordEntries;
    }

    [Test]
    public async Task LexinWords_file_exists_and_is_not_empty()
    {
        // Arrange
        var jsonPath = LexinWordImporter.GetJsonFilePath();

        // Assert
        await Assert.That(File.Exists(jsonPath)).IsTrue();
        
        var entries = LoadWordEntries();
        await Assert.That(entries.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task All_words_have_non_empty_Word_property()
    {
        // Arrange
        var entries = LoadWordEntries();

        // Act
        var entriesWithEmptyWord = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .Where(x => string.IsNullOrWhiteSpace(x.Entry.Word))
            .ToList();

        // Assert
        await Assert.That(entriesWithEmptyWord.Count).IsEqualTo(0);
    }

    [Test]
    public async Task All_words_have_non_empty_Clue_property()
    {
        // Arrange
        var entries = LoadWordEntries();

        // Act
        var entriesWithEmptyClue = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .Where(x => string.IsNullOrWhiteSpace(x.Entry.Clue))
            .ToList();

        // Assert - Report any entries with missing clues
        if (entriesWithEmptyClue.Count > 0)
        {
            var examples = entriesWithEmptyClue.Take(5);
            Console.WriteLine($"Found {entriesWithEmptyClue.Count} entries with empty clues:");
            foreach (var item in examples)
            {
                Console.WriteLine($"  Index {item.Index}: Word='{item.Entry.Word}'");
            }
        }

        await Assert.That(entriesWithEmptyClue.Count).IsEqualTo(0);
    }

    [Test]
    public async Task All_words_have_valid_Category_property()
    {
        // Arrange
        var entries = LoadWordEntries();
        var validCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Substantiv", "Verb", "Adjektiv", "Adverb", "Preposition",
            "Konjunktion", "Pronomen", "Interjektion", "Allmänt", "Idiom"
        };

        // Act
        var entriesWithInvalidCategory = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .Where(x => string.IsNullOrWhiteSpace(x.Entry.Category) || 
                        !validCategories.Contains(x.Entry.Category!))
            .ToList();

        // Assert - Report any entries with invalid categories
        if (entriesWithInvalidCategory.Count > 0)
        {
            var invalidCategories = entriesWithInvalidCategory
                .Select(x => x.Entry.Category ?? "(null)")
                .Distinct()
                .ToList();
            
            Console.WriteLine($"Found {entriesWithInvalidCategory.Count} entries with invalid categories.");
            Console.WriteLine($"Invalid category values: {string.Join(", ", invalidCategories.Take(10))}");
            
            var examples = entriesWithInvalidCategory.Take(5);
            foreach (var item in examples)
            {
                Console.WriteLine($"  Index {item.Index}: Word='{item.Entry.Word}', Category='{item.Entry.Category}'");
            }
        }

        await Assert.That(entriesWithInvalidCategory.Count).IsEqualTo(0);
    }

    [Test]
    public async Task All_words_have_valid_Difficulty_property()
    {
        // Arrange
        var entries = LoadWordEntries();
        var validDifficulties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Easy", "Medium", "Hard"
        };

        // Act
        var entriesWithInvalidDifficulty = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .Where(x => string.IsNullOrWhiteSpace(x.Entry.Difficulty) || 
                        !validDifficulties.Contains(x.Entry.Difficulty!))
            .ToList();

        // Assert - Report any entries with invalid difficulty
        if (entriesWithInvalidDifficulty.Count > 0)
        {
            var invalidDifficulties = entriesWithInvalidDifficulty
                .Select(x => x.Entry.Difficulty ?? "(null)")
                .Distinct()
                .ToList();
            
            Console.WriteLine($"Found {entriesWithInvalidDifficulty.Count} entries with invalid difficulty.");
            Console.WriteLine($"Invalid difficulty values: {string.Join(", ", invalidDifficulties.Take(10))}");
            
            var examples = entriesWithInvalidDifficulty.Take(5);
            foreach (var item in examples)
            {
                Console.WriteLine($"  Index {item.Index}: Word='{item.Entry.Word}', Difficulty='{item.Entry.Difficulty}'");
            }
        }

        await Assert.That(entriesWithInvalidDifficulty.Count).IsEqualTo(0);
    }

    [Test]
    public async Task All_words_contain_only_valid_Swedish_characters()
    {
        // Arrange
        var entries = LoadWordEntries();
        // Valid Swedish uppercase letters: A-Z plus ÅÄÖ
        // Also allow common accented characters from loanwords:
        // É (ABBÉ, CAFÉ), Ê (CRÊPE), Ü (MÜSLI), Ï (NAÏV)
        var validWordPattern = new System.Text.RegularExpressions.Regex(@"^[A-ZÅÄÖÉÊÜÏ]+$");

        // Act
        var entriesWithInvalidChars = entries
            .Select((entry, index) => new { Entry = entry, Index = index })
            .Where(x => !string.IsNullOrEmpty(x.Entry.Word) && 
                        !validWordPattern.IsMatch(x.Entry.Word.ToUpperInvariant()))
            .ToList();

        // Assert - Report any entries with invalid characters
        if (entriesWithInvalidChars.Count > 0)
        {
            Console.WriteLine($"Found {entriesWithInvalidChars.Count} entries with invalid characters:");
            var examples = entriesWithInvalidChars.Take(20);
            foreach (var item in examples)
            {
                var word = item.Entry.Word.ToUpperInvariant();
                var invalidChars = word
                    .Where(c => !System.Text.RegularExpressions.Regex.IsMatch(c.ToString(), @"[A-ZÅÄÖÉÊÜÏ]"))
                    .Distinct()
                    .ToList();
                Console.WriteLine($"  Index {item.Index}: Word='{item.Entry.Word}', Invalid chars: [{string.Join(", ", invalidChars.Select(c => $"'{c}' (U+{(int)c:X4})"))}]");
            }
        }

        await Assert.That(entriesWithInvalidChars.Count).IsEqualTo(0);
    }

    [Test]
    public async Task No_duplicate_words_exist()
    {
        // Arrange
        var entries = LoadWordEntries();

        // Act
        var duplicates = entries
            .Where(e => !string.IsNullOrEmpty(e.Word))
            .GroupBy(e => e.Word.ToUpperInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => new { Word = g.Key, Count = g.Count() })
            .ToList();

        // Assert - Report any duplicates
        if (duplicates.Count > 0)
        {
            Console.WriteLine($"Found {duplicates.Count} duplicate words:");
            foreach (var dup in duplicates.Take(10))
            {
                Console.WriteLine($"  '{dup.Word}' appears {dup.Count} times");
            }
        }

        await Assert.That(duplicates.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Word_lengths_are_within_reasonable_bounds()
    {
        // Arrange
        var entries = LoadWordEntries();
        const int minExpectedLength = 1;
        const int maxExpectedLength = 25; // Reasonable max for crossword words

        // Act
        var outOfBoundsWords = entries
            .Where(e => !string.IsNullOrEmpty(e.Word))
            .Where(e => e.Word.Length < minExpectedLength || e.Word.Length > maxExpectedLength)
            .ToList();

        // Assert - Report any out-of-bounds words
        if (outOfBoundsWords.Count > 0)
        {
            Console.WriteLine($"Found {outOfBoundsWords.Count} words with unusual length:");
            foreach (var word in outOfBoundsWords.Take(10))
            {
                Console.WriteLine($"  '{word.Word}' (length: {word.Word.Length})");
            }
        }

        await Assert.That(outOfBoundsWords.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Clues_are_within_reasonable_length()
    {
        // Arrange
        var entries = LoadWordEntries();
        const int maxExpectedClueLength = 500; // Reasonable max for clue text

        // Act
        var longClues = entries
            .Where(e => !string.IsNullOrEmpty(e.Clue) && e.Clue.Length > maxExpectedClueLength)
            .ToList();

        // Assert - Report any overly long clues
        if (longClues.Count > 0)
        {
            Console.WriteLine($"Found {longClues.Count} clues exceeding {maxExpectedClueLength} characters:");
            foreach (var entry in longClues.Take(5))
            {
                Console.WriteLine($"  Word: '{entry.Word}', Clue length: {entry.Clue.Length}");
            }
        }

        await Assert.That(longClues.Count).IsEqualTo(0);
    }

    [Test]
    public async Task All_difficulty_levels_are_represented()
    {
        // Arrange
        var entries = LoadWordEntries();

        // Act
        var difficultyDistribution = entries
            .Where(e => !string.IsNullOrEmpty(e.Difficulty))
            .GroupBy(e => e.Difficulty!.ToLower())
            .ToDictionary(g => g.Key, g => g.Count());

        // Assert
        Console.WriteLine("Difficulty distribution:");
        foreach (var kvp in difficultyDistribution.OrderBy(x => x.Key))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} words");
        }

        await Assert.That(difficultyDistribution.ContainsKey("easy")).IsTrue();
        await Assert.That(difficultyDistribution.ContainsKey("medium") || difficultyDistribution.ContainsKey("hard")).IsTrue();
    }

    [Test]
    public async Task Multiple_categories_are_represented()
    {
        // Arrange
        var entries = LoadWordEntries();

        // Act
        var categoryDistribution = entries
            .Where(e => !string.IsNullOrEmpty(e.Category))
            .GroupBy(e => e.Category!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Assert
        Console.WriteLine($"Found {categoryDistribution.Count} categories:");
        foreach (var kvp in categoryDistribution.OrderByDescending(x => x.Value).Take(10))
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value} words");
        }

        await Assert.That(categoryDistribution.Count).IsGreaterThan(1);
    }

    [Test]
    public async Task Words_with_Swedish_specific_letters_exist()
    {
        // Arrange
        var entries = LoadWordEntries();

        // Act
        var wordsWithÅ = entries.Count(e => e.Word?.Contains('Å') == true);
        var wordsWithÄ = entries.Count(e => e.Word?.Contains('Ä') == true);
        var wordsWithÖ = entries.Count(e => e.Word?.Contains('Ö') == true);

        // Assert
        Console.WriteLine($"Words containing Swedish-specific letters:");
        Console.WriteLine($"  Å: {wordsWithÅ} words");
        Console.WriteLine($"  Ä: {wordsWithÄ} words");
        Console.WriteLine($"  Ö: {wordsWithÖ} words");

        await Assert.That(wordsWithÅ).IsGreaterThan(0);
        await Assert.That(wordsWithÄ).IsGreaterThan(0);
        await Assert.That(wordsWithÖ).IsGreaterThan(0);
    }
}
