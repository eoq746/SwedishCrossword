using System.Text;
using SwedishCrossword.Models;
using SwedishCrossword.Services;

namespace SwedishCrossword;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Check for command-line arguments for headless operation
        if (args.Length > 0 && args[0] == "--generate-for-web")
        {
            await GenerateForWebHeadless();
            return;
        }

        Console.WriteLine("Svenskt Korsord Generator");
        Console.WriteLine("============================");

        try
        {
            // Initialize services
            var dictionary = new SwedishDictionary();
            var validator = new GridValidator();
            var generator = new CrosswordGenerator(dictionary, validator);
            var clueGenerator = new ClueGenerator();
            var printService = new PrintService(clueGenerator);

            Console.WriteLine($"Ordlista laddad: {dictionary.WordCount:N0} ord");
            Console.WriteLine();

            // Show menu
            while (true)
            {
                Console.WriteLine("Välj alternativ:");
                Console.WriteLine("1. Generera enkelt korsord (11x11) - alla svårighetsgrader");
                Console.WriteLine("2. Generera medel korsord (15x15) - alla svårighetsgrader");
                Console.WriteLine("3. Generera svårt korsord (19x19) - alla svårighetsgrader");
                Console.WriteLine("4. Visa ordlistestatistik");
                Console.WriteLine("5. Importera ord från Lexin (ISOF)");
                Console.WriteLine("6. Generera korsord för webben");
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
                            await GeneratePuzzle(generator, printService, CrosswordGenerationOptions.Easy, "Enkelt");
                            break;

                        case "2":
                            await GeneratePuzzle(generator, printService, CrosswordGenerationOptions.Medium, "Medel");
                            break;

                        case "3":
                            await GeneratePuzzle(generator, printService, CrosswordGenerationOptions.Hard, "Svårt");
                            break;

                        case "4":
                            ShowDictionaryStats(dictionary);
                            break;

                        case "5":
                            await ImportFromLexin();
                            break;

                        case "6":
                            await GenerateForWeb(generator, printService, CrosswordGenerationOptions.Hard);
                            break;

                        case "0":
                            Console.WriteLine("Tack för att du använde Svenskt Korsord Generator!");
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

    /// <summary>
    /// Generates a crossword for web deployment without user interaction.
    /// Used by GitHub Actions for automated daily generation.
    /// </summary>
    private static async Task GenerateForWebHeadless()
    {
        Console.WriteLine("Generating crossword for web (headless mode)...");
        Console.WriteLine($"Generation time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();

        try
        {
            // Initialize services
            var dictionary = new SwedishDictionary();
            var validator = new GridValidator();
            var generator = new CrosswordGenerator(dictionary, validator);
            var clueGenerator = new ClueGenerator();
            var printService = new PrintService(clueGenerator);

            Console.WriteLine($"Dictionary loaded: {dictionary.WordCount:N0} words");

            if (dictionary.WordCount == 0)
            {
                Console.WriteLine("Warning: No words in dictionary, generation may fail");
            }

            // Generate a medium-sized puzzle for web display
            var options = CrosswordGenerationOptions.Medium;
            Console.WriteLine($"Generating {options.Width}x{options.Height} puzzle...");

            var startTime = DateTime.Now;
            var puzzle = await generator.GenerateAsync(options);
            var duration = DateTime.Now - startTime;

            Console.WriteLine();
            Console.WriteLine("Crossword generated successfully!");
            Console.WriteLine($"Time: {duration.TotalSeconds:F1} seconds");
            Console.WriteLine($"Fill percentage: {puzzle.Statistics.FillPercentage:F1}%");
            Console.WriteLine($"Words: {puzzle.Statistics.WordCount}");
            Console.WriteLine();

            // Determine output path - try multiple locations
            var wwwrootPath = FindWwwrootPath();
            Console.WriteLine($"Output directory: {wwwrootPath}");

            if (!Directory.Exists(wwwrootPath))
            {
                Directory.CreateDirectory(wwwrootPath);
                Console.WriteLine($"Created output directory");
            }

            // Save JSON data
            var jsonPath = Path.Combine(wwwrootPath, "puzzle.json");
            await printService.SaveAsJsonAsync(puzzle, jsonPath);
            Console.WriteLine($"JSON saved: {jsonPath}");

            // Verify the file was created
            if (File.Exists(jsonPath))
            {
                var fileInfo = new FileInfo(jsonPath);
                Console.WriteLine($"File verified: {fileInfo.Length} bytes");
            }
            else
            {
                Console.WriteLine("Error: JSON file was not created!");
                Environment.Exit(1);
            }

            Console.WriteLine();
            Console.WriteLine("Web generation complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during generation: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Finds the wwwroot path, checking multiple possible locations
    /// </summary>
    private static string FindWwwrootPath()
    {
        // Try relative to current directory first (when running from project root)
        var paths = new[]
        {
            "SwedishCrossword/wwwroot",
            "wwwroot",
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"),
        };

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        // Default to creating it in current directory
        return Path.GetFullPath("SwedishCrossword/wwwroot");
    }

    private static async Task GeneratePuzzle(
        CrosswordGenerator generator, 
        PrintService printService, 
        CrosswordGenerationOptions options,
        string difficulty)
    {
        Console.WriteLine($"Genererar {difficulty.ToLower()} korsord ({options.Width}x{options.Height})...");
        Console.WriteLine("Detta kan ta en stund...");
        Console.WriteLine();

        var startTime = DateTime.Now;
        var puzzle = await generator.GenerateAsync(options);
        var duration = DateTime.Now - startTime;

        Console.WriteLine("Korsord genererat!");
        Console.WriteLine($"Tid: {duration.TotalSeconds:F1} sekunder");
        Console.WriteLine($"Försök: {puzzle.GenerationAttempts:N0}");
        Console.WriteLine($"Fyllnadsgrad: {puzzle.Statistics.FillPercentage:F1}%");
        Console.WriteLine($"Ord: {puzzle.Statistics.WordCount}");
        Console.WriteLine();

        // Print the puzzle
        var printOptions = PrintOptions.Default;
        var output = printService.GeneratePrintableDocument(puzzle, printOptions);
        Console.WriteLine(output);

        // Ask if user wants to save
        Console.Write("Vill du spara korsordet till fil? (j/n): ");
        if (Console.ReadLine()?.ToLower() == "j")
        {
            var fileName = $"korsord-{difficulty.ToLower()}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            await printService.SaveToFileAsync(puzzle, fileName, printOptions);
            Console.WriteLine($"Sparat som: {fileName}");
        }
    }

    private static async Task GenerateForWeb(
        CrosswordGenerator generator,
        PrintService printService,
        CrosswordGenerationOptions options)
    {
        Console.WriteLine("Genererar korsord för webben...");
        Console.WriteLine();

        var puzzle = await generator.GenerateAsync(options);

        Console.WriteLine("Korsord genererat!");
        Console.WriteLine($"Fyllnadsgrad: {puzzle.Statistics.FillPercentage:F1}%");
        Console.WriteLine($"Ord: {puzzle.Statistics.WordCount}");
        Console.WriteLine();

        // Ensure wwwroot directory exists
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(wwwrootPath))
        {
            // Try relative path from project
            wwwrootPath = "wwwroot";
        }
        
        if (!Directory.Exists(wwwrootPath))
        {
            Directory.CreateDirectory(wwwrootPath);
        }

        // Save JSON data
        var jsonPath = Path.Combine(wwwrootPath, "puzzle.json");
        await printService.SaveAsJsonAsync(puzzle, jsonPath);
        Console.WriteLine($"JSON sparad: {jsonPath}");

        // Also save HTML with embedded data
        var htmlPath = Path.Combine(wwwrootPath, "puzzle.html");
        var html = GenerateStandaloneHtml(puzzle, printService);
        await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false));
        Console.WriteLine($"HTML sparad: {htmlPath}");

        Console.WriteLine();
        Console.WriteLine("För att spela korsordet, öppna filen i en webbläsare:");
        Console.WriteLine($"   file:///{Path.GetFullPath(htmlPath).Replace('\\', '/')}");
    }

    private static string GenerateStandaloneHtml(CrosswordPuzzle puzzle, PrintService printService)
    {
        var json = printService.GenerateJsonForWeb(puzzle);
        
        // Read the template HTML and inject the puzzle data
        var html = GetWebTemplate();
        
        // Replace the sample puzzleData with the real data
        var dataPlaceholder = "const puzzleData = {";
        var dataEndMarker = "};";
        
        var startIndex = html.IndexOf(dataPlaceholder);
        if (startIndex >= 0)
        {
            var endIndex = html.IndexOf(dataEndMarker, startIndex);
            if (endIndex >= 0)
            {
                endIndex += dataEndMarker.Length;
                html = html[..startIndex] + "const puzzleData = " + json + ";" + html[(endIndex)..];
            }
        }
        
        return html;
    }

    /// <summary>
    /// Gets the HTML template by reading from wwwroot/index.html.
    /// This avoids code duplication by using the same template file that's deployed to GitHub Pages.
    /// </summary>
    private static string GetWebTemplate()
    {
        // Try to find index.html in various locations
        var paths = new[]
        {
            "SwedishCrossword/wwwroot/index.html",
            "wwwroot/index.html",
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot", "index.html"),
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                Console.WriteLine($"Using template from: {Path.GetFullPath(path)}");
                var html = File.ReadAllText(path);
                
                // The index.html loads puzzle.json via fetch, but for standalone HTML
                // we need to replace the loadPuzzle function to use embedded data.
                // Find and replace the async loadPuzzle function
                html = ConvertToStandaloneTemplate(html);
                return html;
            }
        }

        throw new FileNotFoundException(
            "Could not find index.html template. Searched paths:\n" + 
            string.Join("\n", paths.Select(p => $"  - {Path.GetFullPath(p)}")));
    }

    /// <summary>
    /// Converts the index.html (which loads puzzle.json via fetch) to a standalone template
    /// that works with embedded puzzle data.
    /// </summary>
    private static string ConvertToStandaloneTemplate(string html)
    {
        // Replace the loadPuzzle function that fetches JSON with one that uses embedded data
        const string originalLoadPuzzle = "async function loadPuzzle()";
        const string newLoadPuzzle = @"async function loadPuzzle() {
            // Standalone mode - puzzleData is embedded in the HTML
            await init();
        }
        
        async function _originalLoadPuzzle()";

        if (html.Contains(originalLoadPuzzle))
        {
            html = html.Replace(originalLoadPuzzle, newLoadPuzzle);
        }

        // Also need to ensure puzzleData is not initialized as a let with default value
        // Change "let puzzleData = {" to "const puzzleData = {" for the embedded version
        html = html.Replace("let puzzleData = {", "const puzzleData = {");

        return html;
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

    private static async Task ImportFromLexin()
    {
        Console.WriteLine("Lexin Import (ISOF Svenska Ordbok)");
        Console.WriteLine("=====================================");
        Console.WriteLine();
        Console.WriteLine("Detta kommer att:");
        Console.WriteLine("  1. Ladda ner Lexin XML-filen (28 MB) om den inte finns");
        Console.WriteLine("  2. Parsa XML och extrahera ord med definitioner");
        Console.WriteLine("  3. Exportera till JSON för snabb laddning");
        Console.WriteLine();
        Console.Write("Vill du fortsätta? (j/n): ");

        if (Console.ReadLine()?.ToLower() != "j")
        {
            Console.WriteLine("Import avbruten.");
            return;
        }

        Console.WriteLine();

        var importer = new LexinWordImporter();
        
        try
        {
            var words = await importer.ImportAndExportAsync();
            
            Console.WriteLine();
            LexinWordImporter.PrintStatistics(words);
            
            Console.WriteLine();
            Console.WriteLine("Import klar!");
            Console.WriteLine("   Starta om programmet för att använda de nya orden.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Import misslyckades: {ex.Message}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Detaljer: {ex.InnerException.Message}");
            }
        }
    }
}
