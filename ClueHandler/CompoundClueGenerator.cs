using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using SwedishCrossword.Services;

namespace ClueHandler;

/// <summary>
/// Generates crossword clues for compound words by parsing the DSSO source file's
/// COMPOUND metadata and combining component-word definitions.
/// </summary>
public static partial class CompoundClueGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [GeneratedRegex(@"^\d+r\d+<([^>]+)>(.+)$")]
    private static partial Regex EntryLineRegex();

    [GeneratedRegex(@"^COMPOUND\([^)]+\):\s*(.+)\s*\+\s*(.+)$")]
    private static partial Regex CompoundLineRegex();

    /// <summary>
    /// Parses chunk files for COMPOUND metadata, generates clues, and updates the JSON.
    /// </summary>
    public static async Task GenerateAsync(string jsonPath, CancellationToken ct = default)
    {
        Console.WriteLine("Steg 1: Parsar COMPOUND-metadata från chunk-filer...");

        // Parse all chunk files to build word → compound parts mapping
        var compoundMap = ParseCompoundMetadata();
        Console.WriteLine($"  Hittade {compoundMap.Count:N0} sammansatta ord med metadata.");

        Console.WriteLine("Steg 2: Laddar ordlista...");
        var json = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8, ct);
        var entries = JsonSerializer.Deserialize<List<WordEntry>>(json, JsonOptions) ?? [];

        // Build a lookup from word → clue for existing definitions
        var clueLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.Clue != "___"))
            clueLookup.TryAdd(e.Word.ToLowerInvariant(), e.Clue);

        Console.WriteLine($"  {entries.Count:N0} ord totalt, {clueLookup.Count:N0} med ledtrådar.");

        Console.WriteLine("Steg 3: Genererar ledtrådar för sammansatta ord...");
        var updated = 0;
        var skipped = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.Clue != "___")
                continue;

            var key = entry.Word.ToLowerInvariant();
            if (!compoundMap.TryGetValue(key, out var parts))
                continue;

            var clue = GenerateCompoundClue(parts.Part1, parts.Part2, clueLookup);
            if (clue is not null)
            {
                entry.Clue = clue;
                // Also add to lookup so subsequent compounds can reference it
                clueLookup.TryAdd(key, clue);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        Console.WriteLine($"  Genererade: {updated:N0}, Hoppade över: {skipped:N0}");

        // Save
        Console.WriteLine("Steg 4: Sparar...");
        await SaveJsonAsync(entries, jsonPath, ct);

        var remaining = entries.Count(e => e.Clue == "___");
        Console.WriteLine();
        Console.WriteLine("Resultat:");
        Console.WriteLine($"  Nya ledtrådar:      {updated:N0}");
        Console.WriteLine($"  Kvar utan ledtråd:  {remaining:N0}");
        Console.WriteLine($"  Sparad till:        {jsonPath}");
    }

    /// <summary>
    /// Parses the DSSO source file and returns a mapping from lowercase word → (part1, part2).
    /// </summary>
    private static Dictionary<string, (string Part1, string Part2)> ParseCompoundMetadata()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        var entryRegex = EntryLineRegex();
        var compoundRegex = CompoundLineRegex();

        var sourceFile = DssoWordImporter.GetSourceFilePath();
        if (!File.Exists(sourceFile))
            return result;

        string? pendingWord = null;

        var lines = File.ReadAllLines(sourceFile, Encoding.Latin1);

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var entryMatch = entryRegex.Match(line);
            if (entryMatch.Success)
            {
                var formsPart = entryMatch.Groups[2].Value;
                var colonIndex = formsPart.IndexOf(':');
                pendingWord = (colonIndex >= 0 ? formsPart[..colonIndex] : formsPart).Trim();
                continue;
            }

            var compoundMatch = compoundRegex.Match(line);
            if (compoundMatch.Success && pendingWord is not null)
            {
                var part1 = compoundMatch.Groups[1].Value.Trim();
                var part2 = compoundMatch.Groups[2].Value.Trim();

                // Only valid crossword words (letters only)
                if (pendingWord.All(char.IsLetter) && pendingWord.Length >= 2)
                {
                    result.TryAdd(pendingWord, (part1, part2));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a crossword clue for a compound word from its parts.
    /// </summary>
    private static string? GenerateCompoundClue(
        string part1, string part2,
        Dictionary<string, string> clueLookup)
    {
        var has1 = clueLookup.TryGetValue(part1, out var def1);
        var has2 = clueLookup.TryGetValue(part2, out var def2);

        // Capitalize part names for display
        var p1 = Capitalize(part1);
        var p2 = Capitalize(part2);

        if (has1 && has2)
        {
            // Both parts have definitions — pick the shorter one to combine
            var shortDef1 = Shorten(def1!, 40);
            var shortDef2 = Shorten(def2!, 40);
            return $"{shortDef1} + {shortDef2.ToLowerInvariant()}";
        }

        // At least format as "Part1 + Part2"
        return $"{p1} + {p2}";
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var cut = text.IndexOfAny([',', ';', '('], 10);
        if (cut > 0 && cut < maxLength)
            return text[..cut].TrimEnd();
        return text[..maxLength].TrimEnd() + "…";
    }

    private static async Task SaveJsonAsync(
        List<WordEntry> entries, string path, CancellationToken ct)
    {
        var output = JsonSerializer.Serialize(entries, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(output);

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(
                    path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                await fs.WriteAsync(bytes, ct);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(1000 * attempt, ct);
            }
        }
    }
}
