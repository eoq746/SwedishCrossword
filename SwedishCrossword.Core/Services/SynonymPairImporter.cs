using System.Text;
using System.Text.Json;
using System.Xml;
using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Imports Swedish synonym pairs from the Folkets synonymlexikon XML file (synpairs.xml).
/// Each synonym pair generates two word entries: Word1 with clue Word2, and Word2 with clue Word1.
/// Source: http://lexikon.nada.kth.se/synlex.html
/// </summary>
public class SynonymPairImporter
{
    /// <summary>
    /// Gets the full path to the synpairs XML file.
    /// </summary>
    public static string GetXmlFilePath() => Path.Combine(DataDirectory.GetPath(), "synpairs.xml");

    /// <summary>
    /// Gets the full path to the synonym pairs JSON file.
    /// </summary>
    public static string GetJsonFilePath() => Path.Combine(DataDirectory.GetPath(), "synonym-words.json");

    /// <summary>
    /// Imports synonym pairs from the synpairs.xml file.
    /// Each pair generates two word entries with cross-references as clues.
    /// </summary>
    /// <param name="xmlPath">Optional custom path to the XML file</param>
    /// <param name="minLevel">Minimum confidence level (1.0-5.0) to include a pair</param>
    /// <returns>List of word entries generated from synonym pairs</returns>
    public async Task<List<WordEntry>> ImportFromXmlAsync(string? xmlPath = null, double minLevel = 3.0)
    {
        var path = xmlPath ?? GetXmlFilePath();

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Synonym pairs XML file not found at: {path}");
        }

        Console.WriteLine($"Parsing synonym pairs XML file: {path}");
        Console.OutputEncoding = Encoding.UTF8;

        var words = new List<WordEntry>();
        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        // The file uses ISO-8859-1 encoding as declared in the XML
        await using var fileStream = File.OpenRead(path);
        using var streamReader = new StreamReader(fileStream, Encoding.Latin1);
        using var reader = XmlReader.Create(streamReader, settings);

        var processedPairs = 0;
        var skippedPairs = 0;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "syn")
            {
                var levelStr = reader.GetAttribute("level");
                if (!double.TryParse(levelStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var level))
                {
                    level = 0;
                }

                // Only process pairs with sufficient confidence level
                if (level < minLevel)
                {
                    skippedPairs++;
                    continue;
                }

                var synXml = await reader.ReadOuterXmlAsync();
                var extractedWords = ParseSynonymPair(synXml, level);

                foreach (var word in extractedWords)
                {
                    if (IsValidCrosswordWord(word.Word))
                    {
                        words.Add(word);
                    }
                }

                processedPairs++;
                if (processedPairs % 5000 == 0)
                {
                    Console.WriteLine($"Processed {processedPairs} synonym pairs, generated {words.Count} word entries...");
                }
            }
        }

        Console.WriteLine($"Finished parsing. Total pairs processed: {processedPairs}");
        Console.WriteLine($"Pairs skipped (low confidence): {skippedPairs}");
        Console.WriteLine($"Word entries generated: {words.Count}");

        return words;
    }

    /// <summary>
    /// Parses a single synonym pair element and extracts two word entries.
    /// </summary>
    private List<WordEntry> ParseSynonymPair(string synXml, double level)
    {
        var words = new List<WordEntry>();

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(synXml);

            var synNode = doc.DocumentElement;
            if (synNode == null) return words;

            var w1Node = synNode.SelectSingleNode("w1");
            var w2Node = synNode.SelectSingleNode("w2");

            var word1 = w1Node?.InnerText?.Trim();
            var word2 = w2Node?.InnerText?.Trim();

            if (string.IsNullOrWhiteSpace(word1) || string.IsNullOrWhiteSpace(word2))
                return words;

            var difficulty = EstimateDifficulty(level);

            // Create entry: Word1 with clue Word2
            words.Add(new WordEntry
            {
                Word = word1.ToUpperInvariant(),
                Clue = CapitalizeFirstLetter(word2),
                Category = "Synonym",
                Difficulty = difficulty.ToString()
            });

            // Create entry: Word2 with clue Word1
            words.Add(new WordEntry
            {
                Word = word2.ToUpperInvariant(),
                Clue = CapitalizeFirstLetter(word1),
                Category = "Synonym",
                Difficulty = difficulty.ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to parse synonym pair: {ex.Message}");
        }

        return words;
    }

    /// <summary>
    /// Checks if a word is suitable for crossword puzzles.
    /// </summary>
    private static bool IsValidCrosswordWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        // Must be at least 1 character
        if (word.Length < 1)
            return false;

        // Must contain only letters (Swedish alphabet)
        // Also allow spaces for multi-word synonyms, but we'll skip those
        foreach (var c in word)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Estimates difficulty based on synonym confidence level.
    /// Higher level means more obvious synonym = easier clue.
    /// </summary>
    private static DifficultyLevel EstimateDifficulty(double level)
    {
        return level switch
        {
            >= 4.5 => DifficultyLevel.Easy,    // Very strong synonym - obvious clue
            >= 4.0 => DifficultyLevel.Easy,
            >= 3.5 => DifficultyLevel.Medium,
            _ => DifficultyLevel.Hard          // Weaker synonyms are harder to guess
        };
    }

    /// <summary>
    /// Capitalizes the first letter of a string.
    /// </summary>
    private static string CapitalizeFirstLetter(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>
    /// Exports imported words to a JSON file for fast loading.
    /// </summary>
    public async Task ExportToJsonAsync(List<WordEntry> words, string? outputPath = null)
    {
        var path = outputPath ?? GetJsonFilePath();

        Console.WriteLine($"Exporting {words.Count} synonym word entries to JSON: {path}");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(words, SafeJsonEncoder.DefaultOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);

        Console.WriteLine($"Export complete: {path}");
    }

    /// <summary>
    /// Full import pipeline: parse XML and export to JSON.
    /// </summary>
    /// <param name="xmlPath">Optional custom path to the XML file</param>
    /// <param name="jsonPath">Optional custom path for the JSON output</param>
    /// <param name="minLevel">Minimum confidence level (1.0-5.0) to include a pair</param>
    public async Task<List<WordEntry>> ImportAndExportAsync(
        string? xmlPath = null,
        string? jsonPath = null,
        double minLevel = 3.0)
    {
        var words = await ImportFromXmlAsync(xmlPath, minLevel);

        // Remove duplicates by word text (keep the first occurrence, which typically has highest confidence)
        var uniqueWords = words
            .GroupBy(w => w.Word)
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"Unique words after deduplication: {uniqueWords.Count}");

        await ExportToJsonAsync(uniqueWords, jsonPath);

        return uniqueWords;
    }

    /// <summary>
    /// Prints statistics about imported synonym words.
    /// </summary>
    public static void PrintStatistics(List<WordEntry> words)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("\n=== Synonym Import Statistics ===");
        Console.WriteLine($"Total word entries: {words.Count}");

        var byLength = words
            .GroupBy(w => w.Word.Length)
            .OrderBy(g => g.Key);

        Console.WriteLine("\nBy word length:");
        foreach (var len in byLength)
        {
            Console.WriteLine($"  {len.Key} letters: {len.Count()}");
        }

        var byDifficulty = words
            .GroupBy(w => w.Difficulty ?? "Unknown")
            .OrderBy(g => g.Key);

        Console.WriteLine("\nBy difficulty:");
        foreach (var diff in byDifficulty)
        {
            Console.WriteLine($"  {diff.Key}: {diff.Count()}");
        }

        // Show some sample words
        Console.WriteLine("\nSample synonym pairs:");
        var random = new Random();
        var samples = words.OrderBy(_ => random.Next()).Take(10);
        foreach (var word in samples)
        {
            Console.WriteLine($"  {word.Word}: {word.Clue}");
        }
    }
}
