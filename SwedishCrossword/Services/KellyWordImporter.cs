using System.Text;
using System.Text.Json;
using System.Xml;
using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Imports Swedish words from the Kelly word list XML file (kelly.xml).
/// The Kelly list contains frequency-ranked vocabulary for Swedish language learners,
/// categorized by CEFR level (A1–C2). Since the Kelly list does not contain definitions,
/// clues are generated from a curated clue dictionary (kelly-clues.json) with POS-based
/// fallback patterns for words not in the dictionary.
/// 
/// Source: Kilgarriff, Adam; Charalabopoulou, Frieda; Gavrilidou, Maria; Johannessen, Janne Bondi;
/// Khalil, Saussan; Kokkinakis, Sofie Johansson; Lew, Robert; Sharoff, Serge; Vadlapudi, Ravikiran
/// &amp; Volodina, Elena. 2014. Corpus-based vocabulary lists for language learners for nine languages.
/// Language Resources and Evaluation, 48:121–163, DOI 10.1007/s10579-013-9251-2.
/// </summary>
public class KellyWordImporter
{
    private Dictionary<string, string>? _clueDictionary;

    /// <summary>
    /// Gets the full path to the Kelly XML file.
    /// </summary>
    public static string GetXmlFilePath() => Path.Combine(DataDirectory.GetPath(), "kelly.xml");

    /// <summary>
    /// Gets the full path to the Kelly words JSON file.
    /// </summary>
    public static string GetJsonFilePath() => Path.Combine(DataDirectory.GetPath(), "kelly-words.json");

    /// <summary>
    /// Gets the full path to the Kelly clue dictionary JSON file.
    /// </summary>
    private static string GetClueDictionaryPath() => Path.Combine(DataDirectory.GetPath(), "kelly-clues.json");

    /// <summary>
    /// Loads the curated clue dictionary from kelly-clues.json.
    /// Keys are lowercased for case-insensitive lookup.
    /// </summary>
    private Dictionary<string, string> LoadClueDictionary()
    {
        var path = GetClueDictionaryPath();
        if (!File.Exists(path))
        {
            Console.WriteLine($"Kelly clue dictionary not found at: {path}");
            return new Dictionary<string, string>();
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dict != null)
            {
                // Normalize keys to lowercase for case-insensitive lookup
                var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in dict)
                {
                    normalized[kvp.Key] = kvp.Value;
                }
                Console.WriteLine($"Loaded {normalized.Count} Kelly clues from dictionary");
                return normalized;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load Kelly clue dictionary: {ex.Message}");
        }

        return new Dictionary<string, string>();
    }

    /// <summary>
    /// Imports words from the Kelly XML file.
    /// Each LexicalEntry produces a word entry with the grundform (gf) as the word text.
    /// Since Kelly does not provide definitions, the clue is left empty.
    /// </summary>
    public async Task<List<WordEntry>> ImportFromXmlAsync(string? xmlPath = null)
    {
        var path = xmlPath ?? GetXmlFilePath();

        if (!File.Exists(path))
            throw new FileNotFoundException($"Kelly XML file not found at: {path}");

        Console.WriteLine($"Parsing Kelly word list XML file: {path}");
        Console.OutputEncoding = Encoding.UTF8;

        var words = new List<WordEntry>();
        var settings = new XmlReaderSettings
        {
            Async = true,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };

        await using var fileStream = File.OpenRead(path);
        using var reader = XmlReader.Create(fileStream, settings);

        var processedEntries = 0;
        var skippedEntries = 0;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "LexicalEntry")
            {
                var entryXml = await reader.ReadOuterXmlAsync();
                var extractedWords = ParseLexicalEntry(entryXml);

                foreach (var word in extractedWords)
                {
                    if (IsValidCrosswordWord(word.Word))
                    {
                        words.Add(word);
                    }
                    else
                    {
                        skippedEntries++;
                    }
                }

                processedEntries++;
                if (processedEntries % 200 == 0)
                {
                    Console.WriteLine($"Processed {processedEntries} Kelly entries, found {words.Count} valid words...");
                }
            }
        }

        Console.WriteLine($"Finished parsing. Total entries processed: {processedEntries}");
        Console.WriteLine($"Valid crossword words: {words.Count}");
        Console.WriteLine($"Skipped entries: {skippedEntries}");

        return words;
    }

    /// <summary>
    /// Parses a single LexicalEntry element and extracts a word entry.
    /// </summary>
    private List<WordEntry> ParseLexicalEntry(string entryXml)
    {
        var words = new List<WordEntry>();

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(entryXml);

            var entryNode = doc.DocumentElement;
            if (entryNode == null) return words;

            var gfNode = entryNode.SelectSingleNode("gf");
            var posNode = entryNode.SelectSingleNode("pos");
            var cefrNode = entryNode.SelectSingleNode("cefr");
            var grammarNode = entryNode.SelectSingleNode("grammar");

            var rawWord = gfNode?.InnerText?.Trim();
            var pos = posNode?.InnerText?.Trim() ?? "";
            var grammar = grammarNode?.InnerText?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(rawWord))
                return words;

            // Some gf values contain parenthetical notes like "vara (vardagl. va)" or
            // "inte (formellt: icke, ej)" – extract just the main word
            var cleanedWord = CleanWordText(rawWord);
            if (string.IsNullOrWhiteSpace(cleanedWord))
                return words;

            int.TryParse(cefrNode?.InnerText?.Trim(), out var cefrLevel);
            var difficulty = MapCefrToDifficulty(cefrLevel);
            var category = MapPosToCategory(pos);

            // Load clue dictionary lazily on first use
            _clueDictionary ??= LoadClueDictionary();

            var clue = GenerateClue(cleanedWord, pos, grammar, _clueDictionary);

            words.Add(new WordEntry
            {
                Word = cleanedWord.ToUpperInvariant(),
                Clue = clue,
                Category = category,
                Difficulty = difficulty.ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to parse Kelly entry: {ex.Message}");
        }

        return words;
    }

    /// <summary>
    /// Generates a crossword clue for a Kelly word. Uses a three-tier strategy:
    /// 1. Curated clue dictionary lookup (best quality)
    /// 2. POS-aware pattern-based clue (e.g., "Att ___" for verbs)
    /// 3. Generic POS description (last resort)
    /// </summary>
    private static string GenerateClue(string word, string pos, string grammar, Dictionary<string, string> clueDictionary)
    {
        // Tier 1: Curated clue dictionary
        if (clueDictionary.TryGetValue(word, out var dictClue))
            return dictClue;

        // Tier 2: POS-aware pattern-based clues
        var posLower = pos.ToLowerInvariant();

        // For nouns, include the article as part of the clue
        if (posLower.StartsWith("noun") || posLower == "noun-en" || posLower == "noun-ett")
        {
            var article = posLower switch
            {
                "noun-en" => "en",
                "noun-ett" => "ett",
                _ => grammar switch
                {
                    "en" => "en",
                    "ett" => "ett",
                    _ => ""
                }
            };

            if (!string.IsNullOrEmpty(article))
                return $"___ ({article}-ord, {word.Length} bokstäver)";

            return $"___ (substantiv, {word.Length} bokstäver)";
        }

        return posLower switch
        {
            "verb" => $"Att ___ ({word.Length} bokstäver)",
            "aux verb" => $"Hjälpverb, att ___",
            "adjective" => $"___ (adjektiv, {word.Length} bokstäver)",
            "adverb" => $"___ (adverb, {word.Length} bokstäver)",
            "prep" => $"___ (preposition)",
            "conj" or "subj" => $"___ (bindeord)",
            "pronoun" => $"___ (pronomen)",
            "det" => $"___ (bestämningsord)",
            "numeral" => $"___ (räkneord)",
            "interj" => $"___ (utrop)",
            "particle" => $"___ (partikel)",
            "proper name" => $"Egennamn ({word.Length} bokstäver)",
            _ => $"Svenskt ord ({word.Length} bokstäver)"
        };
    }

    /// <summary>
    /// Cleans a Kelly word text by removing parenthetical notes and abbreviations.
    /// For example: "vara (vardagl. va)" ? "vara", "de (vardagl. dom)" ? "de"
    /// </summary>
    private static string CleanWordText(string rawWord)
    {
        // Remove everything in parentheses and after
        var parenIdx = rawWord.IndexOf('(');
        if (parenIdx > 0)
            rawWord = rawWord[..parenIdx].Trim();

        // Remove abbreviation notes like "el. i stället", "förk. bl.a."
        var elIdx = rawWord.IndexOf(" el. ", StringComparison.OrdinalIgnoreCase);
        if (elIdx > 0)
            rawWord = rawWord[..elIdx].Trim();

        // Remove any trailing punctuation or whitespace
        rawWord = rawWord.Trim(' ', ',', ';');

        return rawWord;
    }

    /// <summary>
    /// Checks if a word is suitable for crossword puzzles.
    /// </summary>
    private static bool IsValidCrosswordWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        if (word.Length < 1)
            return false;

        // Must contain only letters (Swedish alphabet)
        foreach (var c in word)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Maps CEFR level (1-based integer) to difficulty.
    /// Kelly uses 1 for most common (A1/A2) words up to higher numbers for rarer words.
    /// </summary>
    private static DifficultyLevel MapCefrToDifficulty(int cefrLevel)
    {
        return cefrLevel switch
        {
            1 => DifficultyLevel.Easy,
            2 => DifficultyLevel.Easy,
            3 => DifficultyLevel.Medium,
            4 => DifficultyLevel.Medium,
            _ => DifficultyLevel.Hard
        };
    }

    /// <summary>
    /// Maps Kelly POS (part of speech) tags to crossword categories.
    /// </summary>
    private static string MapPosToCategory(string pos)
    {
        return pos.ToLowerInvariant() switch
        {
            "verb" => "Verb",
            "aux verb" => "Verb",
            "adjective" => "Adjektiv",
            "adverb" => "Adverb",
            "prep" => "Preposition",
            "conj" => "Konjunktion",
            "subj" => "Konjunktion",
            "pronoun" => "Pronomen",
            "det" => "Pronomen",
            "numeral" => "Numeral",
            "interj" => "Interjektion",
            "particle" => "Partikel",
            "proper name" => "Egennamn",
            var p when p.StartsWith("noun") => "Substantiv",
            _ => "Allmänt"
        };
    }

    /// <summary>
    /// Exports imported words to a JSON file for fast loading.
    /// </summary>
    public async Task ExportToJsonAsync(List<WordEntry> words, string? outputPath = null)
    {
        var path = outputPath ?? GetJsonFilePath();

        Console.WriteLine($"Exporting {words.Count} Kelly word entries to JSON: {path}");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(words, SafeJsonEncoder.DefaultOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);

        Console.WriteLine($"Export complete: {path}");
    }

    /// <summary>
    /// Full import pipeline: parse XML and export to JSON.
    /// </summary>
    public async Task<List<WordEntry>> ImportAndExportAsync(string? xmlPath = null, string? jsonPath = null)
    {
        var words = await ImportFromXmlAsync(xmlPath);

        // Remove duplicates by word text (keep the first occurrence)
        var uniqueWords = words
            .GroupBy(w => w.Word)
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"Unique words after deduplication: {uniqueWords.Count}");

        await ExportToJsonAsync(uniqueWords, jsonPath);

        return uniqueWords;
    }

    /// <summary>
    /// Prints statistics about imported Kelly words.
    /// </summary>
    public static void PrintStatistics(List<WordEntry> words)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("\n=== Kelly Word List Import Statistics ===");
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

        Console.WriteLine("\nBy difficulty (CEFR-based):");
        foreach (var diff in byDifficulty)
        {
            Console.WriteLine($"  {diff.Key}: {diff.Count()}");
        }

        var byCategory = words
            .GroupBy(w => w.Category ?? "Unknown")
            .OrderByDescending(g => g.Count());

        Console.WriteLine("\nBy category:");
        foreach (var cat in byCategory)
        {
            Console.WriteLine($"  {cat.Key}: {cat.Count()}");
        }

        // Show some sample words
        Console.WriteLine("\nSample words:");
        var random = new Random();
        var samples = words.OrderBy(_ => random.Next()).Take(10);
        foreach (var word in samples)
        {
            Console.WriteLine($"  {word.Word} ({word.Category}, {word.Difficulty})");
        }
    }
}
