using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Xml;
using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Imports Swedish words from the ISOF Lexin dictionary XML file.
/// Source: https://sprakresurser.isof.se/lexin/svenska/swe_swe.xml
/// </summary>
public class LexinWordImporter
{
    private const string LexinUrl = "https://sprakresurser.isof.se/lexin/svenska/swe_swe.xml";
    
    /// <summary>
    /// Gets the path to the Data directory, working from either the output directory or project directory.
    /// This ensures both the main application and test projects use the same Data folder.
    /// </summary>
    private static string GetDataDirectory()
    {
        // Walk up the directory tree looking for the SwedishCrossword project's Data folder
        var currentDir = AppContext.BaseDirectory;
        
        while (!string.IsNullOrEmpty(currentDir))
        {
            // Look for SwedishCrossword/Data (the source project's data folder)
            var projectDataPath = Path.Combine(currentDir, "SwedishCrossword", "Data");
            if (Directory.Exists(projectDataPath))
            {
                return projectDataPath;
            }
            
            // Check if we're directly in a bin folder of SwedishCrossword project
            // e.g., SwedishCrossword/bin/Debug/net10.0/Data
            if (currentDir.Contains(Path.Combine("SwedishCrossword", "bin")))
            {
                // Walk up to find the SwedishCrossword project root
                var parts = currentDir.Split(Path.DirectorySeparatorChar);
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    if (parts[i] == "SwedishCrossword" && i > 0)
                    {
                        var projectRoot = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Take(i + 1));
                        var dataPath = Path.Combine(projectRoot, "Data");
                        if (Directory.Exists(dataPath))
                        {
                            return dataPath;
                        }
                    }
                }
            }
            
            // Check if we're in the SwedishCrossword directory directly (has the csproj)
            var csprojPath = Path.Combine(currentDir, "SwedishCrossword.csproj");
            var directDataPath = Path.Combine(currentDir, "Data");
            if (File.Exists(csprojPath) && Directory.Exists(directDataPath))
            {
                return directDataPath;
            }
            
            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }
        
        // Last resort: check if output directory has Data (for published apps)
        var outputDataPath = Path.Combine(AppContext.BaseDirectory, "Data");
        if (Directory.Exists(outputDataPath))
        {
            return outputDataPath;
        }
        
        // Fallback: Use the SwedishCrossword project's Data folder, creating it if needed
        // This searches from the solution root
        var solutionDir = FindSolutionDirectory();
        if (solutionDir != null)
        {
            var projectData = Path.Combine(solutionDir, "SwedishCrossword", "Data");
            Directory.CreateDirectory(projectData);
            return projectData;
        }
        
        // Ultimate fallback: create in output directory
        Directory.CreateDirectory(outputDataPath);
        return outputDataPath;
    }
    
    /// <summary>
    /// Finds the solution directory by looking for .sln file
    /// </summary>
    private static string? FindSolutionDirectory()
    {
        var currentDir = AppContext.BaseDirectory;
        
        while (!string.IsNullOrEmpty(currentDir))
        {
            var slnFiles = Directory.GetFiles(currentDir, "*.sln");
            if (slnFiles.Length > 0)
            {
                return currentDir;
            }
            
            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }
        
        return null;
    }

    /// <summary>
    /// Gets the full path to the Lexin XML file.
    /// </summary>
    public static string GetXmlFilePath() => Path.Combine(GetDataDirectory(), "lexin-swe-swe.xml");
    
    /// <summary>
    /// Gets the full path to the Lexin JSON file.
    /// </summary>
    public static string GetJsonFilePath() => Path.Combine(GetDataDirectory(), "lexin-words.json");

    /// <summary>
    /// Downloads the Lexin XML file from ISOF if not already present locally.
    /// </summary>
    public async Task<string> EnsureXmlDownloadedAsync(string? customPath = null)
    {
        var xmlPath = customPath ?? GetXmlFilePath();
        
        if (File.Exists(xmlPath))
        {
            Console.WriteLine($"Using existing XML file: {xmlPath}");
            return xmlPath;
        }

        Console.WriteLine($"Downloading Lexin dictionary from {LexinUrl}...");
        Console.WriteLine("This is a 28MB file, please wait...");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        var response = await httpClient.GetAsync(LexinUrl);
        response.EnsureSuccessStatusCode();

        var directory = Path.GetDirectoryName(xmlPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = File.Create(xmlPath);
        await response.Content.CopyToAsync(fileStream);

        Console.WriteLine($"Downloaded and saved to: {xmlPath}");
        return xmlPath;
    }

    /// <summary>
    /// Imports words from the Lexin XML file using streaming for memory efficiency.
    /// </summary>
    public async Task<List<WordEntry>> ImportFromXmlAsync(string? xmlPath = null)
    {
        var path = xmlPath ?? GetXmlFilePath();
        
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Lexin XML file not found at: {path}. Call EnsureXmlDownloadedAsync first.");
        }

        Console.WriteLine($"Parsing XML file: {path}");
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

        var processedArticles = 0;
        var skippedWords = 0;

        while (await reader.ReadAsync())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "Article")
            {
                var articleXml = await reader.ReadOuterXmlAsync();
                var extractedWords = ParseArticle(articleXml);
                
                foreach (var word in extractedWords)
                {
                    // Filter words suitable for crosswords
                    if (IsValidCrosswordWord(word.Word))
                    {
                        words.Add(word);
                    }
                    else
                    {
                        skippedWords++;
                    }
                }

                processedArticles++;
                if (processedArticles % 1000 == 0)
                {
                    Console.WriteLine($"Processed {processedArticles} articles, found {words.Count} valid words...");
                }
            }
        }

        Console.WriteLine($"Finished parsing. Total articles: {processedArticles}");
        Console.WriteLine($"Valid crossword words: {words.Count}");
        Console.WriteLine($"Skipped words: {skippedWords}");

        return words;
    }

    /// <summary>
    /// Parses a single Article element and extracts word entries.
    /// </summary>
    private List<WordEntry> ParseArticle(string articleXml)
    {
        var words = new List<WordEntry>();

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(articleXml);

            var lemmaNodes = doc.SelectNodes("//Lemma");
            if (lemmaNodes == null) return words;

            foreach (XmlNode lemma in lemmaNodes)
            {
                var wordValue = lemma.Attributes?["Value"]?.Value;
                var wordType = lemma.Attributes?["Type"]?.Value ?? "";

                if (string.IsNullOrWhiteSpace(wordValue))
                    continue;

                // Get all definitions for this lemma
                var lexemeNodes = lemma.SelectNodes(".//Lexeme");
                if (lexemeNodes == null || lexemeNodes.Count == 0)
                    continue;

                foreach (XmlNode lexeme in lexemeNodes)
                {
                    var definitionNode = lexeme.SelectSingleNode("Definition");
                    var definition = definitionNode?.InnerText?.Trim();

                    if (string.IsNullOrWhiteSpace(definition))
                        continue;

                    var category = MapWordTypeToCategory(wordType);
                    var difficulty = EstimateDifficulty(wordValue, definition);

                    words.Add(new WordEntry
                    {
                        Word = wordValue.ToUpperInvariant(),
                        Clue = CapitalizeFirstLetter(definition),
                        Category = category,
                        Difficulty = difficulty.ToString()
                    });

                    // Also extract compound words if present
                    var compoundNodes = lexeme.SelectNodes("Compound");
                    if (compoundNodes != null)
                    {
                        foreach (XmlNode compound in compoundNodes)
                        {
                            var compoundWord = compound.InnerText?.Trim();
                            if (!string.IsNullOrWhiteSpace(compoundWord) && IsValidCrosswordWord(compoundWord))
                            {
                                words.Add(new WordEntry
                                {
                                    Word = compoundWord.ToUpperInvariant(),
                                    Clue = $"Sammansatt ord med {wordValue}",
                                    Category = category,
                                    Difficulty = DifficultyLevel.Medium.ToString()
                                });
                            }
                        }
                    }
                }

                // Also extract idioms
                var idiomNodes = lemma.SelectNodes(".//Idiom");
                if (idiomNodes != null)
                {
                    foreach (XmlNode idiom in idiomNodes)
                    {
                        var idiomText = idiom.FirstChild?.Value?.Trim();
                        var idiomDef = idiom.SelectSingleNode("Definition")?.InnerText?.Trim();

                        if (!string.IsNullOrWhiteSpace(idiomText) && 
                            !string.IsNullOrWhiteSpace(idiomDef) &&
                            IsValidCrosswordWord(idiomText))
                        {
                            words.Add(new WordEntry
                            {
                                Word = idiomText.ToUpperInvariant(),
                                Clue = CapitalizeFirstLetter(idiomDef),
                                Category = "Idiom",
                                Difficulty = DifficultyLevel.Hard.ToString()
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to parse article: {ex.Message}");
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

        // Must be at least 1 character (single letter words are valid in Swedish crosswords)
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
    /// Maps Lexin word types to crossword categories.
    /// </summary>
    private static string MapWordTypeToCategory(string wordType)
    {
        return wordType.ToLowerInvariant() switch
        {
            "subst." => "Substantiv",
            "verb" => "Verb",
            "adj." => "Adjektiv",
            "adv." => "Adverb",
            "prep." => "Preposition",
            "konj." => "Konjunktion",
            "pron." => "Pronomen",
            "interj." => "Interjektion",
            "prefix" => "Prefix",
            "suffix" => "Suffix",
            _ => "Allmänt"
        };
    }

    /// <summary>
    /// Estimates difficulty based on word length and complexity.
    /// </summary>
    private static DifficultyLevel EstimateDifficulty(string word, string definition)
    {
        // Short common words are easy
        if (word.Length <= 4)
            return DifficultyLevel.Easy;

        // Very long words are hard
        if (word.Length >= 10)
            return DifficultyLevel.Hard;

        // Words with many Swedish-specific characters might be harder
        var swedishChars = word.Count(c => "åäöÅÄÖ".Contains(c));
        if (swedishChars >= 2)
            return DifficultyLevel.Medium;

        // Check definition length as proxy for complexity
        if (definition.Length > 50)
            return DifficultyLevel.Medium;

        return DifficultyLevel.Easy;
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

        Console.WriteLine($"Exporting {words.Count} words to JSON: {path}");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(words, options);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8);

        Console.WriteLine($"Export complete: {path}");
    }

    /// <summary>
    /// Full import pipeline: download, parse, and export to JSON.
    /// </summary>
    public async Task<List<WordEntry>> ImportAndExportAsync(
        string? xmlPath = null, 
        string? jsonPath = null,
        bool forceDownload = false)
    {
        var xmlFile = xmlPath ?? GetXmlFilePath();
        
        if (forceDownload && File.Exists(xmlFile))
        {
            File.Delete(xmlFile);
        }

        await EnsureXmlDownloadedAsync(xmlFile);

        var words = await ImportFromXmlAsync(xmlFile);

        // Remove duplicates by word text
        var uniqueWords = words
            .GroupBy(w => w.Word)
            .Select(g => g.First())
            .ToList();

        Console.WriteLine($"Unique words after deduplication: {uniqueWords.Count}");

        await ExportToJsonAsync(uniqueWords, jsonPath);

        return uniqueWords;
    }

    /// <summary>
    /// Prints statistics about imported words.
    /// </summary>
    public static void PrintStatistics(List<WordEntry> words)
    {
        Console.OutputEncoding = Encoding.UTF8;
        
        Console.WriteLine("\n=== Import Statistics ===");
        Console.WriteLine($"Total words: {words.Count}");

        var byCategory = words
            .GroupBy(w => w.Category ?? "Unknown")
            .OrderByDescending(g => g.Count())
            .Take(10);

        Console.WriteLine("\nTop categories:");
        foreach (var cat in byCategory)
        {
            Console.WriteLine($"  {cat.Key}: {cat.Count()}");
        }

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
        Console.WriteLine("\nSample words:");
        var random = new Random();
        var samples = words.OrderBy(_ => random.Next()).Take(10);
        foreach (var word in samples)
        {
            Console.WriteLine($"  {word.Word}: {word.Clue} ({word.Category})");
        }
    }
}
