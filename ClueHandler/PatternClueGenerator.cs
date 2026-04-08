using System.Text;
using System.Text.Json;
using SwedishCrossword.Services;

namespace ClueHandler;

/// <summary>
/// Generates crossword clues for remaining words using:
/// 1. Programmatic compound-word decomposition (finds longest known part split)
/// 2. Swedish morphological patterns (adjective suffixes, noun suffixes, verb forms)
/// </summary>
public static class PatternClueGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = SafeJsonEncoder.DefaultOptions;

    public static async Task GenerateAsync(string jsonPath, CancellationToken ct = default)
    {
        Console.WriteLine("Laddar ordlista...");
        var json = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8, ct);
        var entries = JsonSerializer.Deserialize<List<WordEntry>>(json, JsonOptions) ?? [];

        // Build lookup of all known words with clues
        var clueLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var knownWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            knownWords.Add(e.Word.ToLowerInvariant());
            if (e.Clue != "___")
                clueLookup.TryAdd(e.Word.ToLowerInvariant(), e.Clue);
        }

        var blanks = entries.Where(e => e.Clue == "___").ToList();
        Console.WriteLine($"  {entries.Count:N0} ord totalt, {blanks.Count:N0} saknar ledtråd.");

        var byCompound = 0;
        var byPattern = 0;
        var byPrefix = 0;
        var byPartial = 0;

        foreach (var entry in blanks)
        {
            ct.ThrowIfCancellationRequested();
            var word = entry.Word.ToLowerInvariant();

            // Strategy 1: Try compound decomposition (both parts known)
            var compoundClue = TryDecomposeCompound(word, clueLookup, knownWords, requireBoth: true);
            if (compoundClue is not null)
            {
                entry.Clue = compoundClue;
                clueLookup.TryAdd(word, compoundClue);
                byCompound++;
                continue;
            }

            // Strategy 2: Morphological pattern matching
            var patternClue = TryPatternMatch(word, entry.Category ?? "", clueLookup);
            if (patternClue is not null)
            {
                entry.Clue = patternClue;
                clueLookup.TryAdd(word, patternClue);
                byPattern++;
                continue;
            }

            // Strategy 3: Swedish prefix patterns
            var prefixClue = TryPrefixMatch(word, clueLookup);
            if (prefixClue is not null)
            {
                entry.Clue = prefixClue;
                clueLookup.TryAdd(word, prefixClue);
                byPrefix++;
                continue;
            }

            // Strategy 4: Partial compound (only one part known)
            var partialClue = TryDecomposeCompound(word, clueLookup, knownWords, requireBoth: false);
            if (partialClue is not null)
            {
                entry.Clue = partialClue;
                clueLookup.TryAdd(word, partialClue);
                byPartial++;
                continue;
            }
        }

        Console.WriteLine($"  Sammansättningar:     {byCompound:N0}");
        Console.WriteLine($"  Mönster:              {byPattern:N0}");
        Console.WriteLine($"  Prefix:               {byPrefix:N0}");
        Console.WriteLine($"  Delvis sammansatt:    {byPartial:N0}");

        // Save
        Console.WriteLine("Sparar...");
        await SaveJsonAsync(entries, jsonPath, ct);

        var remaining = entries.Count(e => e.Clue == "___");
        Console.WriteLine();
        Console.WriteLine("Resultat:");
        Console.WriteLine($"  Nya ledtrådar:      {byCompound + byPattern + byPrefix + byPartial:N0}");
        Console.WriteLine($"  Kvar utan ledtråd:  {remaining:N0}");
        Console.WriteLine($"  Sparad till:        {jsonPath}");
    }

    /// <summary>
    /// Tries to split a word into two known parts and generates a compound clue.
    /// Tries to split a word into two known parts and generates a compound clue.
    /// Tries all possible split positions, preferring the split where both parts
    /// are known words with definitions, and the longest part2.
    /// Also handles 's'-joint compounds (e.g., "abonnemangs" + "system").
    /// When <paramref name="requireBoth"/> is false, allows one unknown part.
    /// </summary>
    private static string? TryDecomposeCompound(
        string word, Dictionary<string, string> clueLookup,
        HashSet<string> knownWords, bool requireBoth)
    {
        if (word.Length < 6)
            return null;

        string? bestClue = null;
        int bestScore = -1;

        for (int i = 3; i <= word.Length - 3; i++)
        {
            var part1 = word[..i];
            var part2 = word[i..];

            // Try with various joiners between parts
            string?[] joiners = [null, "s", "es", "o", "e", "u"];
            foreach (var joiner in joiners)
            {
                var actualPart2 = joiner is not null && part2.StartsWith(joiner)
                    ? part2[joiner.Length..]
                    : (joiner is null ? part2 : null);

                if (actualPart2 is null || actualPart2.Length < 3)
                    continue;

                var has1 = clueLookup.TryGetValue(part1, out var def1);
                var has2 = clueLookup.TryGetValue(actualPart2, out var def2);
                var known1 = has1 || knownWords.Contains(part1);
                var known2 = has2 || knownWords.Contains(actualPart2);

                // Also try adding common endings to find the base form
                if (!has1)
                    has1 = TryLookupWithEndings(part1, clueLookup, out def1);
                if (!has2)
                    has2 = TryLookupWithEndings(actualPart2, clueLookup, out def2);

                known1 = known1 || has1;
                known2 = known2 || has2;

                if (requireBoth && (!known1 || !known2))
                    continue;

                if (!requireBoth && !known1 && !known2)
                    continue;

                // Need at least one part with a definition
                if (!has1 && !has2)
                    continue;

                // Score: prefer both having definitions, longer part2
                var score = (has1 ? 100 : 0) + (has2 ? 100 : 0) + actualPart2.Length
                            + (known1 && known2 ? 50 : 0);

                if (score > bestScore)
                {
                    bestScore = score;
                    var p1Display = has1 ? Shorten(def1!, 40) : Capitalize(part1);
                    var p2Display = has2 ? Shorten(def2!, 40).ToLowerInvariant() : actualPart2;
                    bestClue = $"{p1Display} + {p2Display}";
                }
            }
        }

        return bestClue;
    }

    /// <summary>
    /// Tries looking up a word with common Swedish endings appended.
    /// </summary>
    private static bool TryLookupWithEndings(
        string stem, Dictionary<string, string> lookup, out string? definition)
    {
        foreach (var ending in new[] { "", "a", "e", "en", "er", "t", "n", "ar", "or" })
        {
            if (lookup.TryGetValue(stem + ending, out definition))
                return true;
        }
        definition = null;
        return false;
    }

    /// <summary>
    /// Tries common Swedish prefixes and generates a clue from the base word.
    /// </summary>
    private static string? TryPrefixMatch(
        string word, Dictionary<string, string> clueLookup)
    {
        var prefixes = new (string prefix, string meaning)[]
        {
            ("anti",    "Mot"),
            ("för",     "Före-/för-"),
            ("sam",     "Gemensam"),
            ("under",   "Under"),
            ("över",    "Över"),
            ("mellan",  "Mellan"),
            ("efter",   "Efter"),
            ("miss",    "Felaktig"),
            ("van",     "Felaktig/dålig"),
            ("åter",    "Åter-/på nytt"),
            ("om",      "Om-/på nytt"),
            ("bi",      "Sido-/bi-"),
            ("mot",     "Mot"),
            ("med",     "Med-"),
            ("ut",      "Ut-"),
            ("in",      "In-"),
            ("av",      "Av-"),
            ("till",    "Till-"),
            ("bak",     "Bak-/bakåt"),
            ("stor",    "Stor"),
            ("små",     "Liten"),
            ("ny",      "Ny"),
            ("half",    "Halv"),
            ("halv",    "Halv"),
            ("hel",     "Hel"),
            ("mång",    "Mång-/fler-"),
            ("en",      "En-/ensam-"),
            ("super",   "Mycket/över"),
            ("hyper",   "Överdrivet"),
            ("ultra",   "Extremt"),
            ("mini",    "Mycket liten"),
            ("mikro",   "Extremt liten"),
            ("makro",   "Mycket stor"),
            ("multi",   "Mång-/fler-"),
            ("poly",    "Mång-"),
            ("mono",    "En-/ensam-"),
            ("pseudo",  "Falsk/sken-"),
            ("kvasi",   "Skenbar"),
            ("neo",     "Ny-"),
            ("pre",     "Före"),
            ("post",    "Efter"),
            ("re",      "Åter-/på nytt"),
            ("sub",     "Under"),
            ("trans",   "Över/genom"),
            ("inter",   "Mellan"),
            ("extra",   "Utöver/utanför"),
            ("kontra",  "Mot-"),
        };

        foreach (var (prefix, meaning) in prefixes)
        {
            if (!word.StartsWith(prefix) || word.Length <= prefix.Length + 2)
                continue;

            var remainder = word[prefix.Length..];
            if (clueLookup.TryGetValue(remainder, out var def) ||
                TryLookupWithEndings(remainder, clueLookup, out def))
            {
                return $"{meaning} {Shorten(def!, 50).ToLowerInvariant()}";
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to generate a clue from Swedish morphological patterns.
    /// </summary>
    private static string? TryPatternMatch(
        string word, string category, Dictionary<string, string> clueLookup)
    {
        // ── Adjectives ────────────────────────────────────
        if (category == "Adjektiv")
        {
            // -isk → "Relaterad till {base}"
            if (word.EndsWith("isk") && TryFindBase(word, ["isk", "iska"], clueLookup, out var baseDef))
                return $"Som har med {baseDef.ToLowerInvariant()} att göra";

            if (word.EndsWith("istisk") && TryFindBase(word, ["istisk"], clueLookup, out baseDef))
                return $"Som har med {baseDef.ToLowerInvariant()} att göra";

            // -lig → "Som är {base}-artad"
            if (word.EndsWith("lig") && TryFindBase(word, ["lig", "liga"], clueLookup, out baseDef))
                return $"Som kan beskrivas som {baseDef.ToLowerInvariant()}";

            // -bar → "Som kan {verb}s"
            if (word.EndsWith("bar") && TryFindBase(word, ["bar"], clueLookup, out baseDef))
                return $"Möjlig att {baseDef.ToLowerInvariant()}";

            // -aktig → "Liknande {base}"
            if (word.EndsWith("aktig") && TryFindBase(word, ["aktig"], clueLookup, out baseDef))
                return $"Liknande {baseDef.ToLowerInvariant()}";

            // -mässig → "I enlighet med {base}"
            if (word.EndsWith("mässig") && TryFindBase(word, ["mässig", "smässig"], clueLookup, out baseDef))
                return $"I enlighet med {baseDef.ToLowerInvariant()}";

            // -betonad → "Med betoning på {base}"
            if (word.EndsWith("betonad") && TryFindBase(word, ["betonad", "sbetonad"], clueLookup, out baseDef))
                return $"Med betoning på {baseDef.ToLowerInvariant()}";

            // -artad → "Av {base}s art"
            if (word.EndsWith("artad") && TryFindBase(word, ["artad", "sardad"], clueLookup, out baseDef))
                return $"Av {baseDef.ToLowerInvariant()} art";

            // -förande → "Som för {base}"
            if (word.EndsWith("förande") && TryFindBase(word, ["förande", "sförande"], clueLookup, out baseDef))
                return $"Som för {baseDef.ToLowerInvariant()}";

            // -bunden → "Bunden till {base}"
            if (word.EndsWith("bunden") && TryFindBase(word, ["bunden", "sbunden"], clueLookup, out baseDef))
                return $"Bunden till {baseDef.ToLowerInvariant()}";

            // -fri → "Utan {base}"
            if (word.EndsWith("fri") && TryFindBase(word, ["fri", "sfri"], clueLookup, out baseDef))
                return $"Utan {baseDef.ToLowerInvariant()}";

            // -rik → "Med mycket {base}"
            if (word.EndsWith("rik") && TryFindBase(word, ["rik", "srik"], clueLookup, out baseDef))
                return $"Med mycket {baseDef.ToLowerInvariant()}";

            // -fattig → "Med lite {base}"
            if (word.EndsWith("fattig") && TryFindBase(word, ["fattig", "sfattig"], clueLookup, out baseDef))
                return $"Med lite {baseDef.ToLowerInvariant()}";

            // -lös → "Utan {base}"
            if (word.EndsWith("lös") && TryFindBase(word, ["lös", "slös"], clueLookup, out baseDef))
                return $"Utan {baseDef.ToLowerInvariant()}";

            // -full → "Full av {base}"
            if (word.EndsWith("full") && TryFindBase(word, ["full", "sfull"], clueLookup, out baseDef))
                return $"Full av {baseDef.ToLowerInvariant()}";

            // -formig → "Med formen av {base}"
            if (word.EndsWith("formig") && TryFindBase(word, ["formig", "sformig"], clueLookup, out baseDef))
                return $"Med formen av {baseDef.ToLowerInvariant()}";

            // -färgad → "Med färg av {base}"
            if (word.EndsWith("färgad") && TryFindBase(word, ["färgad", "sfärgad"], clueLookup, out baseDef))
                return $"Med färg av {baseDef.ToLowerInvariant()}";

            // -liknande → "Som liknar {base}"
            if (word.EndsWith("liknande") && TryFindBase(word, ["liknande", "sliknande"], clueLookup, out baseDef))
                return $"Som liknar {baseDef.ToLowerInvariant()}";

            // -relaterad → "Med anknytning till {base}"
            if (word.EndsWith("relaterad") && TryFindBase(word, ["relaterad", "srelaterad"], clueLookup, out baseDef))
                return $"Med anknytning till {baseDef.ToLowerInvariant()}";
        }

        // ── Nouns ─────────────────────────────────────────
        if (category == "Substantiv")
        {
            // -ning → noun from verb
            if (word.EndsWith("ning") && TryFindBase(word, ["ning", "aning", "kning"], clueLookup, out var baseDef))
                return $"Handlingen att {baseDef.ToLowerInvariant()}";

            // -het → abstract noun from adjective
            if (word.EndsWith("het") && TryFindBase(word, ["het", "ighet"], clueLookup, out baseDef))
                return $"Egenskapen att vara {baseDef.ToLowerInvariant()}";

            // -skap → abstract noun
            if (word.EndsWith("skap") && TryFindBase(word, ["skap"], clueLookup, out baseDef))
                return $"Tillståndet av {baseDef.ToLowerInvariant()}";

            // -tion → Latinate noun
            if (word.EndsWith("tion") && TryFindBase(word, ["tion"], clueLookup, out baseDef))
                return $"Handlingen att {baseDef.ToLowerInvariant()}";

            // -ism → ideology/system
            if (word.EndsWith("ism") && TryFindBase(word, ["ism"], clueLookup, out baseDef))
                return $"Lära om {baseDef.ToLowerInvariant()}";

            // -ist → person associated with -ism
            if (word.EndsWith("ist") && TryFindBase(word, ["ist"], clueLookup, out baseDef))
                return $"Anhängare av {baseDef.ToLowerInvariant()}";

            // -eri → place or activity noun
            if (word.EndsWith("eri") && TryFindBase(word, ["eri"], clueLookup, out baseDef))
                return $"Verksamhet med {baseDef.ToLowerInvariant()}";
        }

        // ── Verbs ─────────────────────────────────────────
        if (category == "Verb")
        {
            // -era → often foreign-origin verb
            if (word.EndsWith("era") && TryFindBase(word, ["era"], clueLookup, out var baseDef))
                return $"Utföra {baseDef.ToLowerInvariant()}";

            // -isera → make into
            if (word.EndsWith("isera") && TryFindBase(word, ["isera"], clueLookup, out baseDef))
                return $"Göra till {baseDef.ToLowerInvariant()}";

            // -iera
            if (word.EndsWith("iera") && TryFindBase(word, ["iera"], clueLookup, out baseDef))
                return $"Utföra {baseDef.ToLowerInvariant()}";

            // -göra
            if (word.EndsWith("göra") && TryFindBase(word, ["göra", "sgöra"], clueLookup, out baseDef))
                return $"Göra {baseDef.ToLowerInvariant()}";
        }

        // ── Proper nouns (name patterns) ──────────────────
        if (category == "Egennamn")
        {
            // Patronymics
            if (word.EndsWith("sson"))
                return $"Svenskt efternamn (son till {Capitalize(word[..^4])})";
            if (word.EndsWith("sdotter"))
                return $"Svenskt efternamn (dotter till {Capitalize(word[..^7])})";
            if (word.EndsWith("dotter"))
                return $"Svenskt efternamn (dotter till {Capitalize(word[..^6])})";
        }

        return null;
    }

    /// <summary>
    /// Tries stripping known suffixes and looking up the base form in the clue dictionary.
    /// </summary>
    private static bool TryFindBase(
        string word, string[] suffixes,
        Dictionary<string, string> clueLookup,
        out string baseDef)
    {
        foreach (var suffix in suffixes)
        {
            if (word.Length <= suffix.Length + 2)
                continue;

            var stem = word[..^suffix.Length];

            // Try exact stem
            if (clueLookup.TryGetValue(stem, out var def))
            {
                baseDef = Shorten(def, 50);
                return true;
            }

            // Try adding common endings: -a, -e, -en, -er
            foreach (var ending in new[] { "a", "e", "en", "er", "t" })
            {
                if (clueLookup.TryGetValue(stem + ending, out def))
                {
                    baseDef = Shorten(def, 50);
                    return true;
                }
            }
        }

        baseDef = "";
        return false;
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
