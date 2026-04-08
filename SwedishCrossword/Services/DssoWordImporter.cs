using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Imports Swedish words from the DSSO (Den Stora Svenska Ordlistan) text file
/// (dsso-1.51.txt).
///
/// File format:
///   - Lines starting with '#' are comments.
///   - Word entry lines: ID&lt;category&gt;word:form1:form2:...
///   - DEFINITION N: clue text (optional, follows a word entry)
///   - CUSTOM / COMPOUND / BASEWORDS lines are metadata (skipped).
///
/// Source: https://dsso.se/ — licensed under CC BY-SA 3.0.
/// </summary>
public partial class DssoWordImporter
{
    /// <summary>
    /// Regex that matches the main entry line:
    ///   digits + 'r' + digits + '&lt;category&gt;' + word-forms separated by ':'
    /// </summary>
    [GeneratedRegex(@"^\d+r\d+<([^>]+)>(.+)$")]
    private static partial Regex EntryLineRegex();

    [GeneratedRegex(@"^DEFINITION\s+\d+:\s*(.+)$")]
    private static partial Regex DefinitionLineRegex();

    /// <summary>
    /// Gets the full path to the DSSO words JSON file.
    /// </summary>
    public static string GetJsonFilePath() => Path.Combine(DataDirectory.GetPath(), "dsso-words.json");

    /// <summary>
    /// Gets the full path to the DSSO source text file.
    /// </summary>
    public static string GetSourceFilePath() => Path.Combine(DataDirectory.GetPath(), "dsso-1.51.txt");

    /// <summary>
    /// Imports words from the DSSO source file.
    /// </summary>
    public List<WordEntry> ImportFromFile(string? filePath = null)
    {
        var path = filePath ?? GetSourceFilePath();

        if (!File.Exists(path))
        {
            Console.WriteLine($"DSSO source file not found: {path}");
            return [];
        }

        Console.WriteLine($"Importing from DSSO source file: {Path.GetFileName(path)}");

        var allWords = ParseSourceFile(path);
        Console.WriteLine($"  {Path.GetFileName(path)}: {allWords.Count} words");

        // Deduplicate by upper-cased word, keeping the first occurrence that has a real clue
        var deduplicated = new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in allWords)
        {
            var key = entry.Word;
            if (!deduplicated.TryGetValue(key, out var existing))
            {
                deduplicated[key] = entry;
            }
            else if (existing.Clue == "___" && entry.Clue != "___")
            {
                // Prefer the entry with a real clue
                deduplicated[key] = entry;
            }
        }

        var result = deduplicated.Values.ToList();
        Console.WriteLine($"Total unique words after deduplication: {result.Count}");
        return result;
    }

    /// <summary>
    /// Parses the DSSO source file and returns word entries.
    /// </summary>
    private List<WordEntry> ParseSourceFile(string filePath)
    {
        var words = new List<WordEntry>();
        var lines = File.ReadAllLines(filePath, Encoding.Latin1);
        var entryRegex = EntryLineRegex();
        var defRegex = DefinitionLineRegex();

        string? pendingWord = null;
        string? pendingCategory = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var entryMatch = entryRegex.Match(line);
            if (entryMatch.Success)
            {
                // Flush previous entry if it had no definition
                if (pendingWord != null)
                {
                    AddWordEntry(words, pendingWord, "___", pendingCategory);
                }

                var category = entryMatch.Groups[1].Value;
                var formsPart = entryMatch.Groups[2].Value;

                // The first colon-separated value is the base/display word
                var colonIndex = formsPart.IndexOf(':');
                var baseWord = colonIndex >= 0 ? formsPart[..colonIndex] : formsPart;

                pendingWord = baseWord.Trim();
                pendingCategory = MapCategory(category);
                continue;
            }

            var defMatch = defRegex.Match(line);
            if (defMatch.Success && pendingWord != null)
            {
                var definition = defMatch.Groups[1].Value.Trim();
                AddWordEntry(words, pendingWord, CapitalizeFirstLetter(definition), pendingCategory);
                pendingWord = null;
                pendingCategory = null;
                continue;
            }

            // Metadata lines (CUSTOM, COMPOUND, BASEWORDS) — skip but don't flush
            if (line.StartsWith("CUSTOM:") || line.StartsWith("COMPOUND(") || line.StartsWith("BASEWORDS:"))
                continue;

            // Additional DEFINITION lines for same word — skip (we already took the first)
            if (defRegex.IsMatch(line))
                continue;

            // Unknown line — flush pending entry without definition
            if (pendingWord != null)
            {
                AddWordEntry(words, pendingWord, "___", pendingCategory);
                pendingWord = null;
                pendingCategory = null;
            }
        }

        // Flush the last pending entry
        if (pendingWord != null)
        {
            AddWordEntry(words, pendingWord, "___", pendingCategory);
        }

        return words;
    }

    private static void AddWordEntry(List<WordEntry> words, string word, string clue, string? category)
    {
        if (!IsValidCrosswordWord(word))
            return;

        words.Add(new WordEntry
        {
            Word = word.ToUpperInvariant(),
            Clue = clue,
            Category = category ?? "Allmänt",
            Difficulty = EstimateDifficulty(word).ToString()
        });
    }

    /// <summary>
    /// Checks if a word is suitable for crossword puzzles (letters only, no spaces/hyphens/digits).
    /// </summary>
    private static bool IsValidCrosswordWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
            return false;

        foreach (var c in word)
        {
            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Maps DSSO category tags to the category names used in the dictionary.
    /// </summary>
    private static string MapCategory(string dssoCategory)
    {
        return dssoCategory.ToLowerInvariant() switch
        {
            "substantiv" => "Substantiv",
            "verb" => "Verb",
            "adjektiv" => "Adjektiv",
            "adverb" => "Adverb",
            "preposition" => "Preposition",
            "konjunktion" => "Konjunktion",
            "pronomen" => "Pronomen",
            "interjektion" => "Interjektion",
            "egennamn" => "Egennamn",
            "förkortning" => "Förkortning",
            _ => "Allmänt"
        };
    }

    /// <summary>
    /// Estimates difficulty based on word length.
    /// </summary>
    private static DifficultyLevel EstimateDifficulty(string word)
    {
        if (word.Length <= 4)
            return DifficultyLevel.Easy;
        if (word.Length >= 10)
            return DifficultyLevel.Hard;
        return DifficultyLevel.Medium;
    }

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

        Console.WriteLine($"Exporting {words.Count} DSSO words to JSON: {path}");

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
    /// Full import pipeline: parse source file and export to JSON.
    /// </summary>
    public async Task<List<WordEntry>> ImportAndExportAsync(
        string? sourcePath = null,
        string? jsonPath = null)
    {
        var words = ImportFromFile(sourcePath);
        await ExportToJsonAsync(words, jsonPath);
        return words;
    }

    /// <summary>
    /// Prints statistics about the imported words.
    /// </summary>
    public static void PrintStatistics(List<WordEntry> words)
    {
        Console.WriteLine("DSSO Import Statistics");
        Console.WriteLine("======================");
        Console.WriteLine($"Total words: {words.Count}");

        var withClue = words.Count(w => w.Clue != "___");
        var withoutClue = words.Count(w => w.Clue == "___");
        Console.WriteLine($"Words with definition (clue): {withClue}");
        Console.WriteLine($"Words without definition (___): {withoutClue}");
        Console.WriteLine();

        var byCategory = words.GroupBy(w => w.Category)
            .OrderByDescending(g => g.Count());
        Console.WriteLine("By category:");
        foreach (var group in byCategory)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }

        Console.WriteLine();

        var byDifficulty = words.GroupBy(w => w.Difficulty)
            .OrderBy(g => g.Key);
        Console.WriteLine("By difficulty:");
        foreach (var group in byDifficulty)
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");
        }
    }
}
