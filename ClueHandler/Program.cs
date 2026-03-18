using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClueHandler;
using SwedishCrossword.Services;

internal class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Support direct command-line invocation for non-interactive tasks
        if (args.Length > 0 && args[0] == "--wiktionary")
        {
            await PopulateCluesFromWiktionaryAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--compounds")
        {
            await CompoundClueGenerator.GenerateAsync(
                DssoWordImporter.GetJsonFilePath());
            return;
        }

        if (args.Length > 0 && args[0] == "--patterns")
        {
            await PatternClueGenerator.GenerateAsync(
                DssoWordImporter.GetJsonFilePath());
            return;
        }

        Console.WriteLine("Svenskt Korsord Clue Handler");
        Console.WriteLine("============================");

        try
        {
            // Initialize services
            var dictionary = new SwedishDictionary();
            //var validator = new GridValidator();
            //var generator = new CrosswordGenerator(dictionary, validator);
            //var clueGenerator = new ClueGenerator();
            //var printService = new PrintService(clueGenerator);

            Console.WriteLine($"Ordlista laddad: {dictionary.WordCount:N0} ord");
            Console.WriteLine();

            // Show menu
            while (true)
            {
                Console.WriteLine("Välj alternativ:");
                Console.WriteLine("1. Visa ordlistestatistik");
                Console.WriteLine("2. Lägg till nya ord");
                Console.WriteLine("3. Redigera ledtrådar");
                Console.WriteLine("4. Hämta ledtrådar från Wiktionary (automatiskt)");
                Console.WriteLine("0. Avsluta");
                Console.WriteLine();
                Console.Write("Ditt val: ");

                var choice = Console.ReadLine();
                Console.WriteLine();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            ShowDictionaryStats(dictionary);
                            break;

                        case "2":
                            AddNewWords();
                            break;

                        case "3":
                            ModifyClues();
                            break;

                        case "4":
                            await PopulateCluesFromWiktionaryAsync();
                            break;

                        case "0":
                            Console.WriteLine("Tack för att du använde Svenskt Korsord Clue Handler!");
                            return;

                        default:
                            Console.WriteLine("Ogiltigt val. Försök igen.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fel: {ex.Message}");
                    Console.WriteLine("Försöker igen...");
                }

                Console.WriteLine();
                Console.WriteLine("Tryck på valfri tangent för att fortsätta...");
                Console.ReadKey();
                Console.Clear();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Kritiskt fel: {ex.Message}");
            Console.WriteLine("Programmet avslutas.");
        }
    }

    private static string GetCustomWordsFilePath()
        => Path.Combine(DataDirectory.GetPath(), "custom-words.json");

    private static void ModifyClues()
    {
        var files = new Dictionary<string, string>
        {
            ["1"] = KellyWordImporter.GetJsonFilePath(),
            ["2"] = LexinWordImporter.GetJsonFilePath(),
            ["3"] = SynonymPairImporter.GetJsonFilePath(),
            ["4"] = GetCustomWordsFilePath(),
            ["5"] = DssoWordImporter.GetJsonFilePath()
        };

        Console.WriteLine("Tillgängliga filer med ledtrådar:");
        foreach (var (key, path) in files)
        {
            var exists = File.Exists(path) ? "OK" : "Not OK";
            Console.WriteLine($"  {key}. [{exists}] {path}");
        }
        Console.WriteLine();
        Console.Write($"Välj fil (1-{files.Count}): ");

        var fileChoice = Console.ReadLine()?.Trim();
        if (fileChoice is null || !files.TryGetValue(fileChoice, out var filePath))
        {
            Console.WriteLine("Ogiltigt val.");
            return;
        }

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Filen finns inte: {filePath}");
            return;
        }

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        var entries = JsonSerializer.Deserialize<List<WordEntry>>(json, JsonOptions) ?? [];

        if (entries.Count == 0)
        {
            Console.WriteLine("Filen innehåller inga ord.");
            return;
        }

        Console.Write("Sök efter ord (Enter för att visa alla): ");
        var search = Console.ReadLine()?.Trim().ToUpperInvariant();

        Console.Write("Filtrera på ordlängd (Enter för alla): ");
        var lengthInput = Console.ReadLine()?.Trim();
        int.TryParse(lengthInput, out var lengthFilter);

        Console.Write("Filtrera på ledtråd (Enter för alla): ");
        var clueFilter = Console.ReadLine()?.Trim();

        var targets = entries
            .Select((entry, index) => (entry, index))
            .Where(x => string.IsNullOrEmpty(search) || x.entry.Word.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(x => lengthFilter <= 0 || x.entry.Word.Length == lengthFilter)
            .Where(x => string.IsNullOrEmpty(clueFilter) || x.entry.Clue.Contains(clueFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (targets.Count == 0)
        {
            Console.WriteLine("Inga matchande ord hittades.");
            return;
        }

        Console.WriteLine($"Hittade {targets.Count} ord. Tryck Enter för att hoppa över. Skriv 'q' för att avsluta. Skriv 'del' för att ta bort ordet.");
        Console.WriteLine();

        var modified = 0;
        var removed = 0;
        var indicesToRemove = new HashSet<int>();
        var current = 0;
        foreach (var (entry, index) in targets)
        {
            current++;
            Console.WriteLine($"[{current}/{targets.Count}] Ord: {entry.Word} ({entry.Category})");
            Console.WriteLine($"  Nuvarande: {entry.Clue}");
            Console.Write("  Ny ledtråd (eller 'del' för att ta bort): ");

            var input = Console.ReadLine()?.Trim();

            if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Avbryter redigering...");
                break;
            }

            if (string.Equals(input, "del", StringComparison.OrdinalIgnoreCase))
            {
                indicesToRemove.Add(index);
                removed++;
                Console.WriteLine($"  ✗ Markerad för borttagning");
            }
            else if (!string.IsNullOrEmpty(input))
            {
                entries[index].Clue = input;
                modified++;
                Console.WriteLine("  + Uppdaterad");
            }
            else
            {
                Console.WriteLine("  — Hoppade över");
            }
            Console.WriteLine();
        }

        if (removed > 0)
        {
            entries = entries.Where((_, i) => !indicesToRemove.Contains(i)).ToList();
            Console.WriteLine($"Tog bort {removed} ord.");
        }

        if (modified > 0 || removed > 0)
        {
            var output = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(filePath, output, Encoding.UTF8);
            Console.WriteLine($"Sparade ändringar ({modified} uppdaterade, {removed} borttagna) till: {filePath}");
        }
        else
        {
            Console.WriteLine("Inga ändringar gjordes.");
        }
    }

    private static void AddNewWords()
    {
        var outputPath = GetCustomWordsFilePath();

        List<WordEntry> existing = [];
        if (File.Exists(outputPath))
        {
            var json = File.ReadAllText(outputPath, Encoding.UTF8);
            existing = JsonSerializer.Deserialize<List<WordEntry>>(json, JsonOptions) ?? [];
            Console.WriteLine($"Befintlig fil laddad med {existing.Count} ord: {outputPath}");
        }
        else
        {
            Console.WriteLine($"Ny fil kommer skapas vid sparning: {outputPath}");
        }

        Console.WriteLine();
        Console.WriteLine("Lägg till nya ord. Skriv 'q' som ord för att avsluta.");
        Console.WriteLine("OBS: Kom ihåg att uppdatera ordimportören för att inkludera custom-words.json.");
        Console.WriteLine();

        var added = 0;
        while (true)
        {
            Console.Write("Ord (versaler, 'q' för att avsluta): ");
            var rawInput = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(rawInput) || string.Equals(rawInput, "q", StringComparison.OrdinalIgnoreCase))
                break;

            var word = rawInput.ToUpperInvariant();

            if (!word.All(char.IsLetter))
            {
                Console.WriteLine("  Ogiltigt ord. Endast bokstäver tillåtna.");
                continue;
            }

            if (existing.Any(e => e.Word.Equals(word, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine($"  Ordet '{word}' finns redan i listan. Hoppar över.");
                continue;
            }

            Console.Write("Ledtråd: ");
            var clue = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(clue))
            {
                Console.WriteLine("  Ledtråd krävs. Hoppar över.");
                continue;
            }

            Console.Write("Kategori (Substantiv/Verb/Adjektiv/Adverb/Annat) [Substantiv]: ");
            var category = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(category))
                category = "Substantiv";

            Console.Write("Svårighetsgrad (Easy/Medium/Hard) [Easy]: ");
            var difficulty = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(difficulty))
                difficulty = "Easy";

            existing.Add(new WordEntry
            {
                Word = word,
                Clue = clue,
                Category = category,
                Difficulty = difficulty
            });
            added++;
            Console.WriteLine($"  ✓ Tillagt ({added} nya denna session)");
            Console.WriteLine();
        }

        if (added > 0)
        {
            var output = JsonSerializer.Serialize(existing, JsonOptions);
            File.WriteAllText(outputPath, output, Encoding.UTF8);
            Console.WriteLine($"Sparade {existing.Count} ord (varav {added} nya) till: {outputPath}");
            Console.WriteLine();
            Console.WriteLine("Glöm inte att uppdatera SwedishDictionary för att ladda custom-words.json!");
        }
        else
        {
            Console.WriteLine("Inga ord lades till.");
        }
    }

    private static async Task PopulateCluesFromWiktionaryAsync()
    {
        var jsonPath = DssoWordImporter.GetJsonFilePath();

        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"Filen finns inte: {jsonPath}");
            return;
        }

        Console.WriteLine("Hämtar ledtrådar från svenska Wiktionary för alla ord med '___'.");
        Console.WriteLine("Framsteg sparas automatiskt. Avbryt med Ctrl+C.");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\nAvbryter... sparar framsteg.");
        };

        using var service = new WiktionaryClueService();

        try
        {
            await service.PopulateFromDumpAsync(jsonPath, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Processen avbröts. Framsteg har sparats.");
        }
    }

    private static void ShowDictionaryStats(SwedishDictionary dictionary)
    {
        if (dictionary.WordCount == 0)
        {
            Console.WriteLine("Ordlistestatistik");
            Console.WriteLine("==================");
            Console.WriteLine();
            Console.WriteLine("Ordlistan är tom!");
            Console.WriteLine();
            Console.WriteLine("För att ladda ord, välj alternativ 5 'Importera ord från Lexin (ISOF)'");
            Console.WriteLine($"Förväntad sökväg: {LexinWordImporter.GetJsonFilePath()}");
            return;
        }

        var stats = dictionary.GetStatistics();

        Console.WriteLine("Ordlistestatistik");
        Console.WriteLine("==================");
        Console.WriteLine($"Totalt antal ord: {stats.TotalWords:N0}");
        Console.WriteLine($"Kategorier: {stats.Categories.Count}");
        Console.WriteLine($"Genomsnittlig längd: {stats.AverageLength:F1} bokstäver");
        Console.WriteLine($"Längdspann: {stats.MinLength}-{stats.MaxLength} bokstäver");
        Console.WriteLine($"Datakälla: {LexinWordImporter.GetJsonFilePath()}");
        Console.WriteLine();

        Console.WriteLine("Fördelning per svårighetsgrad:");
        foreach (var difficulty in stats.DifficultyDistribution.OrderBy(d => d.Key))
        {
            Console.WriteLine($"  {difficulty.Key}: {difficulty.Value:N0} ord");
        }
        Console.WriteLine();

        Console.WriteLine("Största kategorier:");
        foreach (var category in stats.Categories.OrderByDescending(c => c.Value).Take(10))
        {
            Console.WriteLine($"  {category.Key}: {category.Value:N0} ord");
        }
        Console.WriteLine();

        Console.WriteLine("Fördelning per längd:");
        foreach (var length in stats.LengthDistribution.OrderBy(l => l.Key))
        {
            var bar = new string('#', Math.Min(50, length.Value / 50 + 1));
            Console.WriteLine($"  {length.Key,2} bokstäver: {length.Value,5:N0} ord {bar}");
        }
    }
}