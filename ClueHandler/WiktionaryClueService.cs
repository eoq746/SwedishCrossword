using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using ICSharpCode.SharpZipLib.BZip2;
using SwedishCrossword.Services;

namespace ClueHandler;

/// <summary>
/// Looks up Swedish word definitions from the Swedish Wiktionary (sv.wiktionary.org).
///
/// Primary mode: downloads the full Wiktionary XML dump (~30 MB bz2) and parses it
/// locally — no API calls, no rate limits, processes all words in minutes.
///
/// Fallback mode: uses the MediaWiki batch query API (50 words per request).
/// </summary>
public partial class WiktionaryClueService : IDisposable
{
    private const string DumpUrl =
        "https://dumps.wikimedia.org/svwiktionary/latest/svwiktionary-latest-pages-articles.xml.bz2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _httpClient;

    public WiktionaryClueService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "SwedishCrosswordClueBot/1.0 (Educational project)");
    }

    // ── Dump-based approach (primary) ────────────────────────────────

    /// <summary>
    /// Downloads the Swedish Wiktionary dump, parses it locally, and
    /// fills in blank ("___") clues.  No API rate limits apply.
    /// </summary>
    public async Task PopulateFromDumpAsync(string jsonPath, CancellationToken ct = default)
    {
        Console.WriteLine($"Laddar ord från: {jsonPath}");
        var json = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8, ct);
        var entries = JsonSerializer.Deserialize<List<WordEntry>>(json, JsonOptions) ?? [];

        // Build a lookup of words that still need clues (upper-cased key)
        var needed = new Dictionary<string, List<WordEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.Clue == "___"))
        {
            var key = e.Word.ToLowerInvariant();
            if (!needed.ContainsKey(key))
                needed[key] = [];
            needed[key].Add(e);
        }

        Console.WriteLine($"Totalt: {entries.Count:N0} ord, varav {needed.Values.Sum(v => v.Count):N0} saknar ledtråd.");

        if (needed.Count == 0)
        {
            Console.WriteLine("Alla ord har redan ledtrådar!");
            return;
        }

        // Step 1 — Download + decompress + parse the dump
        Console.WriteLine();
        Console.WriteLine($"Laddar ner Wiktionary-dump från:");
        Console.WriteLine($"  {DumpUrl}");
        Console.WriteLine("(~30 MB komprimerad, kan ta 1-2 min beroende på anslutning)");
        Console.WriteLine();

        var definitions = await ParseDumpAsync(needed, ct);

        // Step 2 — Apply definitions
        var found = 0;
        foreach (var (word, def) in definitions)
        {
            if (needed.TryGetValue(word, out var entriesToFix))
            {
                foreach (var entry in entriesToFix)
                {
                    entry.Clue = def;
                    found++;
                }
            }
        }

        // Step 3 — Save
        await SaveJsonAsync(entries, jsonPath, ct);

        var remaining = entries.Count(e => e.Clue == "___");
        Console.WriteLine();
        Console.WriteLine("Resultat:");
        Console.WriteLine($"  Nya ledtrådar:      {found:N0}");
        Console.WriteLine($"  Kvar utan ledtråd:  {remaining:N0}");
        Console.WriteLine($"  Sparad till:        {jsonPath}");
    }

    /// <summary>
    /// Streams the bz2-compressed Wiktionary XML dump, decompresses on the
    /// fly, and extracts definitions for words in <paramref name="needed"/>.
    /// </summary>
    private async Task<Dictionary<string, string>> ParseDumpAsync(
        Dictionary<string, List<WordEntry>> needed, CancellationToken ct)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pagesScanned = 0;

        // Download to a temp file first so the bz2 stream can seek if needed
        var tempPath = Path.Combine(Path.GetTempPath(), "svwiktionary-dump.xml.bz2");

        if (File.Exists(tempPath) &&
            File.GetLastWriteTimeUtc(tempPath) > DateTime.UtcNow.AddDays(-7))
        {
            Console.WriteLine($"Använder cachad dump: {tempPath}");
        }
        else
        {
            Console.Write("Laddar ner...");
            using var httpStream = await _httpClient.GetStreamAsync(DumpUrl, ct);
            using var fileStream = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 81920);
            await httpStream.CopyToAsync(fileStream, ct);
            Console.WriteLine(" klar!");
        }

        Console.Write("Parsar XML-dump...");

        using var compressedStream = File.OpenRead(tempPath);
        using var bz2Stream = new BZip2InputStream(compressedStream);
        using var reader = XmlReader.Create(bz2Stream,
            new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore });

        while (await reader.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();

            if (reader.NodeType != XmlNodeType.Element || reader.Name != "page")
                continue;

            // Read the full <page> subtree
            string? title = null;
            string? text = null;

            using var pageReader = reader.ReadSubtree();
            while (await pageReader.ReadAsync())
            {
                if (pageReader.NodeType != XmlNodeType.Element)
                    continue;

                switch (pageReader.Name)
                {
                    case "title":
                        title = await pageReader.ReadElementContentAsStringAsync();
                        break;
                    case "text":
                        text = await pageReader.ReadElementContentAsStringAsync();
                        break;
                }
            }

            pagesScanned++;

            if (pagesScanned % 10_000 == 0)
                Console.Write($"\r  Sidor skannade: {pagesScanned:N0}, hittade: {results.Count:N0}   ");

            if (title is null || text is null)
                continue;

            // Only process pages matching words we need
            if (!needed.ContainsKey(title))
                continue;

            var definition = ExtractDefinition(text);
            if (definition is not null)
                results[title] = definition;
        }

        Console.WriteLine($"\r  Sidor skannade: {pagesScanned:N0}, hittade: {results.Count:N0}   ");
        Console.WriteLine("Klar!");

        return results;
    }

    // ── Wikitext parsing ──────────────────────────────────────────────

    /// <summary>
    /// Extracts the first definition line from the ==Svenska== section.
    /// </summary>
    private static string? ExtractDefinition(string wikitext)
    {
        var lines = wikitext.Split('\n');
        bool inSwedishSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Top-level section header (== ... ==)
            if (line.StartsWith("==") && !line.StartsWith("==="))
            {
                inSwedishSection = line.Contains("Svenska", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSwedishSection)
                continue;

            // Definition lines start with '#' but not '#:', '#*', '##'
            if (line.StartsWith('#') &&
                !line.StartsWith("#:") &&
                !line.StartsWith("#*") &&
                !line.StartsWith("##"))
            {
                var definition = line.TrimStart('#').Trim();
                definition = CleanWikiMarkup(definition);

                if (!string.IsNullOrWhiteSpace(definition) && definition.Length > 1)
                {
                    definition = char.ToUpperInvariant(definition[0]) + definition[1..];

                    // Trim very long definitions for crossword use
                    if (definition.Length > 120)
                    {
                        var cutoff = definition.IndexOfAny([',', ';'], 20);
                        if (cutoff > 0 && cutoff < 120)
                            definition = definition[..cutoff];
                    }

                    return definition;
                }
            }
        }

        return null;
    }

    // ── Regex helpers for cleaning wiki markup ────────────────────────

    [GeneratedRegex(@"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]")]
    private static partial Regex WikiLinkRegex();

    [GeneratedRegex(@"\{\{tagg\|[^}]*\}\}\s*")]
    private static partial Regex TaggTemplateRegex();

    [GeneratedRegex(@"\{\{[^}]*\}\}")]
    private static partial Regex TemplateRegex();

    [GeneratedRegex(@"<ref[^>]*>.*?</ref>", RegexOptions.Singleline)]
    private static partial Regex RefTagRegex();

    [GeneratedRegex(@"<ref[^>]*/>")]
    private static partial Regex SelfClosingRefRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static string CleanWikiMarkup(string text)
    {
        text = WikiLinkRegex().Replace(text, "$1");
        text = TaggTemplateRegex().Replace(text, "");
        text = TemplateRegex().Replace(text, "");
        text = text.Replace("'''", "").Replace("''", "");
        text = RefTagRegex().Replace(text, "");
        text = SelfClosingRefRegex().Replace(text, "");
        text = HtmlTagRegex().Replace(text, "");
        text = WhitespaceRegex().Replace(text, " ").Trim();
        return text;
    }

    // ── Persistence ──────────────────────────────────────────────────

    private static async Task SaveJsonAsync(
        List<WordEntry> entries, string path, CancellationToken ct)
    {
        var output = JsonSerializer.Serialize(entries, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(output);

        // Retry up to 3 times to handle file locks (e.g. Visual Studio holding the file)
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
