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

            //Console.WriteLine($"Ordlista laddad: {dictionary.WordCount:N0} ord");
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
                Console.WriteLine("6. Importera synonympar (Folkets synonymlexikon)");
                Console.WriteLine("7. Importera ord från Kelly-listan");
                Console.WriteLine("8. Generera korsord för webben");
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
                            await ImportSynonymPairs();
                            break;

                        case "7":
                            await ImportKellyWords();
                            break;

                        case "8":
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
        Console.WriteLine($"Working directory: {Environment.CurrentDirectory}");
        Console.WriteLine($"Base directory: {AppContext.BaseDirectory}");
        Console.WriteLine();

        try
        {
            // Print data file locations for debugging
            var lexinPath = LexinWordImporter.GetJsonFilePath();
            var synonymPath = SynonymPairImporter.GetJsonFilePath();
            Console.WriteLine($"Lexin dictionary path: {lexinPath}");
            Console.WriteLine($"Lexin file exists: {File.Exists(lexinPath)}");
            Console.WriteLine($"Synonym dictionary path: {synonymPath}");
            Console.WriteLine($"Synonym file exists: {File.Exists(synonymPath)}");
            Console.WriteLine();

            // Initialize services
            var dictionary = new SwedishDictionary();
            var validator = new GridValidator();
            var generator = new CrosswordGenerator(dictionary, validator);
            var clueGenerator = new ClueGenerator();
            var printService = new PrintService(clueGenerator);

            Console.WriteLine($"Dictionary loaded: {dictionary.WordCount:N0} words");

            if (dictionary.WordCount == 0)
            {
                Console.WriteLine("ERROR: No words in dictionary!");
                Console.WriteLine("The crossword cannot be generated without words.");
                Console.WriteLine();
                Console.WriteLine("Ensure the Data directory contains:");
                Console.WriteLine($"  - {Path.GetFileName(lexinPath)}");
                Console.WriteLine($"  - {Path.GetFileName(synonymPath)}");
                Environment.Exit(1);
            }

            if (dictionary.WordCount < 1000)
            {
                Console.WriteLine($"WARNING: Only {dictionary.WordCount} words loaded. Expected 50,000+");
            }

            // Generate a hard-sized puzzle for web display
            var options = CrosswordGenerationOptions.Hard;
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

        //// Adjust options for easier testing
        //Console.WriteLine("Justerar inställningar för snabbare generering under utveckling... Kom ihåg att ta bort detta innan produktion!");
        //options.TargetFillPercentage = 0.5;

        var puzzle = await generator.GenerateAsync(options);

        Console.WriteLine("Korsord genererat!");
        Console.WriteLine($"Fyllnadsgrad: {puzzle.Statistics.FillPercentage:F1}%");
        Console.WriteLine($"Ord: {puzzle.Statistics.WordCount}");
        Console.WriteLine($"Number of vinkelord: {puzzle.Statistics.VinkelOrd}");
        Console.WriteLine();

        // Find the bin output wwwroot directory
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        
        if (!Directory.Exists(wwwrootPath))
        {
            Directory.CreateDirectory(wwwrootPath);
        }

        // Save puzzle.json to a temp file first, then atomically replace
        var jsonPath = Path.Combine(wwwrootPath, "puzzle.json");
        var tempJsonPath = Path.GetTempFileName();
        await printService.SaveAsJsonAsync(puzzle, tempJsonPath);
        CopyAndReplace(tempJsonPath, jsonPath);
        Console.WriteLine($"JSON sparad: {jsonPath}");

        // Copy files from source wwwroot to output directory
        var sourceWwwroot = FindSourceWwwroot();
        if (sourceWwwroot != null)
        {
            // Files to copy from source wwwroot
            var filesToCopy = new[]
            {
                "index.html",
                "site.min.css",
                "site.js",
                "om-oss.html",
                "kontakt.html",
                "integritetspolicy.html",
                "sitemap.xml",
                "robots.txt",
                "ads.txt",
                "CNAME",
                "favicon.ico",
                "favicon-16x16.png",
                "favicon-32x32.png",
                "apple-touch-icon.png",
                "android-chrome-192x192.png",
                "android-chrome-512x512.png",
                "site.webmanifest"
            };

            foreach (var fileName in filesToCopy)
            {
                var sourcePath = Path.Combine(sourceWwwroot, fileName);
                if (File.Exists(sourcePath))
                {
                    var outputPath = Path.Combine(wwwrootPath, fileName);
                    CopyAndReplace(sourcePath, outputPath);
                    Console.WriteLine($"Kopierad: {fileName}");
                }
            }
        }

        Console.WriteLine();
        
        // Ask if user wants to start a local web server
        Console.Write("Vill du starta en lokal webbserver? (j/n): ");
        if (Console.ReadLine()?.ToLower() == "j")
        {
            await StartLocalWebServer(wwwrootPath);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("För att spela korsordet, starta en webbserver manuellt:");
            Console.WriteLine($"   cd \"{Path.GetFullPath(wwwrootPath)}\"");
            Console.WriteLine("   http-server .");
            Console.WriteLine("   Öppna sedan: http://localhost:8080/");
        }
    }

    /// <summary>
    /// Copies a file to the destination, using atomic replace if the destination exists.
    /// </summary>
    private static void CopyAndReplace(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file not found", sourcePath);
        }

        var destinationDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destinationPath))
        {
            // Use atomic replace when destination exists
            var tempPath = destinationPath + ".tmp";
            File.Copy(sourcePath, tempPath, overwrite: true);
            File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
        }
        else
        {
            // Simple copy when destination doesn't exist
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    /// <summary>
    /// Finds the source wwwroot directory by walking up from the executable location
    /// </summary>
    private static string? FindSourceWwwroot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            // Skip bin/obj directories
            if (!dir.FullName.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && 
                !dir.FullName.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                !dir.FullName.EndsWith(Path.DirectorySeparatorChar + "bin") &&
                !dir.FullName.EndsWith(Path.DirectorySeparatorChar + "obj"))
            {
                var candidate = Path.Combine(dir.FullName, "wwwroot");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
                {
                    return candidate;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static async Task<bool> TryStartHttpServer(string workingDirectory)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c http-server .",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                Console.WriteLine("http-server startad. Tryck Ctrl+C för att avsluta.");
                await process.WaitForExitAsync();
                return true;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // http-server not found, try Python
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fel vid start av http-server: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Starts a local HTTP server to serve the crossword files.
    /// Tries http-server (npm) first, then falls back to Python.
    /// </summary>
    private static async Task StartLocalWebServer(string wwwrootPath)
    {
        var fullPath = Path.GetFullPath(wwwrootPath);
        const string url = "http://localhost:8080";
        
        Console.WriteLine();
        Console.WriteLine("Startar lokal webbserver...");
        Console.WriteLine($"Mapp: {fullPath}");
        Console.WriteLine($"URL: {url}");
        Console.WriteLine();
        Console.WriteLine("Tryck Ctrl+C för att stoppa servern.");
        Console.WriteLine();

        // Try to open the browser
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            Console.WriteLine($"Kunde inte öppna webbläsaren automatiskt. Öppna {url} manuellt.");
        }

        // Try http-server first (npm package)
        if (await TryStartHttpServer(fullPath))
        {
            return;
        }

        // Try Python as fallback
        if (await TryStartPythonServer(fullPath))
        {
            return;
        }
        
        Console.WriteLine("Kunde inte starta någon webbserver.");
        Console.WriteLine("Installera http-server: npm install -g http-server");
        Console.WriteLine("Eller använd Python: python -m http.server 8080");
    }

    private static async Task<bool> TryStartPythonServer(string workingDirectory)
    {
        // Try python first, then python3 (Linux/Mac)
        var pythonCommands = new[] { "python", "python3", "py" };

        foreach (var python in pythonCommands)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "-m http.server 8080",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    Console.WriteLine($"{python} http.server startad. Tryck Ctrl+C för att avsluta.");
                    await process.WaitForExitAsync();
                    return true;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // This python command not found, try next
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fel vid start av {python}: {ex.Message}");
            }
        }

        return false;
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

    private static async Task ImportSynonymPairs()
    {
        Console.WriteLine("Synonympar Import (Folkets synonymlexikon)");
        Console.WriteLine("============================================");
        Console.WriteLine();
        Console.WriteLine("Detta kommer att:");
        Console.WriteLine("  1. Parsa synpairs.xml och extrahera synonympar");
        Console.WriteLine("  2. Skapa två ordposter per par (ord1->ord2, ord2->ord1)");
        Console.WriteLine("  3. Exportera till JSON för snabb laddning");
        Console.WriteLine();
        Console.WriteLine($"Förväntad XML-fil: {SynonymPairImporter.GetXmlFilePath()}");
        Console.WriteLine();

        if (!File.Exists(SynonymPairImporter.GetXmlFilePath()))
        {
            Console.WriteLine("VARNING: synpairs.xml hittades inte!");
            Console.WriteLine("Ladda ner filen från: http://lexikon.nada.kth.se/synlex.html");
            Console.WriteLine($"Och placera den i: {Path.GetDirectoryName(SynonymPairImporter.GetXmlFilePath())}");
            return;
        }

        Console.Write("Ange minsta konfidensnivå (1.0-5.0, standard 3.0): ");
        var levelInput = Console.ReadLine();
        var minLevel = 3.0;
        if (!string.IsNullOrWhiteSpace(levelInput) && double.TryParse(levelInput, 
            System.Globalization.NumberStyles.Float, 
            System.Globalization.CultureInfo.InvariantCulture, out var parsedLevel))
        {
            minLevel = Math.Clamp(parsedLevel, 1.0, 5.0);
        }

        Console.WriteLine();
        Console.Write($"Vill du importera synonympar med nivå >= {minLevel:F1}? (j/n): ");

        if (Console.ReadLine()?.ToLower() != "j")
        {
            Console.WriteLine("Import avbruten.");
            return;
        }

        Console.WriteLine();

        var importer = new SynonymPairImporter();
        
        try
        {
            var words = await importer.ImportAndExportAsync(minLevel: minLevel);
            
            Console.WriteLine();
            SynonymPairImporter.PrintStatistics(words);
            
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

    private static async Task ImportKellyWords()
    {
        Console.WriteLine("Kelly Import (Frekvensbaserad ordlista för språkinlärare)");
        Console.WriteLine("==========================================================");
        Console.WriteLine();
        Console.WriteLine("Kelly-listan innehåller frekvensbaserade svenska ord kategoriserade efter CEFR-nivå.");
        Console.WriteLine("Källa: Kilgarriff et al. (2014). Corpus-based vocabulary lists for language learners");
        Console.WriteLine("       for nine languages. Language Resources and Evaluation, 48:121–163.");
        Console.WriteLine();
        Console.WriteLine("OBS: Kelly-listan innehåller inga definitioner. Orden läggs till för ordvalidering");
        Console.WriteLine("     (t.ex. för att verifiera oavsiktliga ord i korsordet).");
        Console.WriteLine();
        Console.WriteLine("Detta kommer att:");
        Console.WriteLine("  1. Parsa kelly.xml och extrahera ord med CEFR-nivåer");
        Console.WriteLine("  2. Exportera till JSON för snabb laddning");
        Console.WriteLine();
        Console.WriteLine($"Förväntad XML-fil: {KellyWordImporter.GetXmlFilePath()}");
        Console.WriteLine();

        if (!File.Exists(KellyWordImporter.GetXmlFilePath()))
        {
            Console.WriteLine("VARNING: kelly.xml hittades inte!");
            Console.WriteLine($"Placera filen i: {Path.GetDirectoryName(KellyWordImporter.GetXmlFilePath())}");
            return;
        }

        Console.Write("Vill du fortsätta? (j/n): ");

        if (Console.ReadLine()?.ToLower() != "j")
        {
            Console.WriteLine("Import avbruten.");
            return;
        }

        Console.WriteLine();

        var importer = new KellyWordImporter();
        
        try
        {
            var words = await importer.ImportAndExportAsync();
            
            Console.WriteLine();
            KellyWordImporter.PrintStatistics(words);
            
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
