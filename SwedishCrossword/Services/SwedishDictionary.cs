using System.Text.Json;
using SwedishCrossword.Models;
using System.Text;
using System.Text.Encodings.Web;

namespace SwedishCrossword.Services;

/// <summary>
/// Service for managing Swedish words and their clues
/// </summary>
public class SwedishDictionary
{
    private readonly Dictionary<string, WordEntry> _words;
    private readonly Random _random = new();

    public IReadOnlyList<Word> AllWords => _words.Values.Select(ConvertToWord).ToList().AsReadOnly();
    public int WordCount => _words.Count;

    public SwedishDictionary()
        : this(false)
    {
    }

    public SwedishDictionary(bool empty)
    {
        _words = new Dictionary<string, WordEntry>();

        if (!empty)
        {
            // Try to load Lexin words (if they've been imported)
            var lexinJsonPath = LexinWordImporter.GetJsonFilePath();
            if (File.Exists(lexinJsonPath))
            {
                LoadWordsFromFile(lexinJsonPath);
                Console.WriteLine($"Loaded Lexin dictionary: {WordCount} words");
            }
            else
            {
                Console.WriteLine($"Lexin dictionary not found at: {lexinJsonPath}");
                Console.WriteLine("Run 'Import from Lexin' option to download and import words.");
            }
        }

        Console.WriteLine($"Total words loaded: {WordCount}");
    }

    private void LoadWordsFromFile(string filePath)
    {
        try
        {
            Console.WriteLine($"Loading words from: {Path.GetFileName(filePath)}");
            
            string jsonText = "";
            Encoding encoding = Encoding.UTF8;
            
            // Try UTF-8 first
            try
            {
                jsonText = File.ReadAllText(filePath, Encoding.UTF8);
                Console.WriteLine("Successfully read file using UTF-8 encoding");
                Console.WriteLine($"File loaded using: UTF-8");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UTF-8 failed: {ex.Message}");
            }

            var wordData = JsonSerializer.Deserialize<List<WordEntry>>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            if (wordData != null)
            {
                int wordsAdded = 0;
                foreach (var entry in wordData)
                {
                    if (!string.IsNullOrWhiteSpace(entry.Word) && !_words.ContainsKey(entry.Word.ToUpperInvariant()))
                    {
                        _words[entry.Word.ToUpperInvariant()] = entry;
                        wordsAdded++;
                    }
                }
                Console.WriteLine($"Loaded {wordsAdded} words successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading words from {filePath}: {ex.Message}");
        }
    }

    private Word ConvertToWord(WordEntry entry)
    {
        if (Enum.TryParse<DifficultyLevel>(entry.Difficulty, true, out var difficulty))
        {
            return new Word(entry.Word, entry.Clue, entry.Category ?? "", difficulty);
        }
        return new Word(entry.Word, entry.Clue, entry.Category ?? "", DifficultyLevel.Medium);
    }

    /// <summary>
    /// Gets words filtered by various criteria
    /// </summary>
    public IEnumerable<Word> GetWords(
        int? minLength = null,
        int? maxLength = null,
        string? category = null,
        DifficultyLevel? difficulty = null)
    {
        var query = _words.Values.AsEnumerable();

        if (minLength.HasValue)
            query = query.Where(w => w.Word.Length >= minLength.Value);

        if (maxLength.HasValue)
            query = query.Where(w => w.Word.Length <= maxLength.Value);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(w => (w.Category ?? "").Equals(category, StringComparison.OrdinalIgnoreCase));

        if (difficulty.HasValue)
            query = query.Where(w => Enum.TryParse<DifficultyLevel>(w.Difficulty, true, out var diff) && diff == difficulty.Value);

        return query.Select(ConvertToWord);
    }

    /// <summary>
    /// Gets words that contain a specific letter at a specific position
    /// </summary>
    public IEnumerable<Word> GetWordsWithLetterAt(char letter, int position)
    {
        return _words.Values
            .Where(w => 
                position >= 0 && 
                position < w.Word.Length && 
                w.Word[position] == char.ToUpper(letter))
            .Select(ConvertToWord);
    }

    /// <summary>
    /// Gets words that contain a specific letter anywhere
    /// </summary>
    public IEnumerable<Word> GetWordsWithLetter(char letter)
    {
        return _words.Values
            .Where(w => w.Word.Contains(char.ToUpper(letter)))
            .Select(ConvertToWord);
    }

    /// <summary>
    /// Gets random words from the dictionary
    /// </summary>
    public IEnumerable<Word> GetRandomWords(int count, IEnumerable<Word>? excludeWords = null)
    {
        var excludeWordTexts = excludeWords?.Select(w => w.Text.ToUpperInvariant()).ToHashSet() ?? [];
        var availableWords = _words.Values
            .Where(w => !excludeWordTexts.Contains(w.Word.ToUpperInvariant()))
            .ToList();

        if (availableWords.Count == 0)
            return [];

        count = Math.Min(count, availableWords.Count);
        var shuffled = availableWords
            .OrderBy(x => _random.Next())
            .Take(count)
            .Select(ConvertToWord);

        return shuffled;
    }

    /// <summary>
    /// Finds words that can intersect with a given word
    /// </summary>
    public IEnumerable<Word> FindIntersectingWords(Word word, char sharedLetter)
    {
        return _words.Values
            .Where(w => 
                !w.Word.Equals(word.Text, StringComparison.OrdinalIgnoreCase) && 
                w.Word.Contains(sharedLetter))
            .Select(ConvertToWord)
            .Where(w => !w.IsPlaced);
    }

    /// <summary>
    /// Gets words suitable for starting a crossword (good letters for intersections)
    /// </summary>
    public IEnumerable<Word> GetStarterWords(int maxLength = 8)
    {
        // Prefer words with common Swedish letters and vowels
        var commonLetters = new HashSet<char> { 'A', 'E', 'I', 'O', 'U', 'R', 'S', 'T', 'N', 'L' };
        
        return _words.Values
            .Where(w => w.Word.Length <= maxLength && w.Word.Length >= 3)
            .Where(w => w.Word.Count(c => commonLetters.Contains(c)) >= w.Word.Length / 2)
            .OrderByDescending(w => w.Word.Count(c => commonLetters.Contains(c)))
            .Select(ConvertToWord);
    }

    /// <summary>
    /// Gets dictionary statistics
    /// </summary>
    public DictionaryStats GetStatistics()
    {
        if (_words.Count == 0)
        {
            return new DictionaryStats
            {
                TotalWords = 0,
                Categories = new Dictionary<string, int>(),
                LengthDistribution = new Dictionary<int, int>(),
                DifficultyDistribution = new Dictionary<DifficultyLevel, int>(),
                AverageLength = 0,
                MinLength = 0,
                MaxLength = 0
            };
        }
        
        var stats = new DictionaryStats
        {
            TotalWords = _words.Count,
            Categories = _words.Values.GroupBy(w => w.Category ?? "Unknown")
                              .ToDictionary(g => g.Key, g => g.Count()),
            LengthDistribution = _words.Values.GroupBy(w => w.Word.Length)
                                     .ToDictionary(g => g.Key, g => g.Count()),
            DifficultyDistribution = _words.Values
                .GroupBy(w => Enum.TryParse<DifficultyLevel>(w.Difficulty, true, out var diff) ? diff : DifficultyLevel.Medium)
                .ToDictionary(g => g.Key, g => g.Count()),
            AverageLength = _words.Values.Average(w => w.Word.Length),
            MinLength = _words.Values.Min(w => w.Word.Length),
            MaxLength = _words.Values.Max(w => w.Word.Length)
        };

        return stats;
    }

    /// <summary>
    /// Adds a custom word to the dictionary
    /// </summary>
    public void AddWord(string text, string clue, string category = "", DifficultyLevel difficulty = DifficultyLevel.Medium)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(clue))
            throw new ArgumentException("Word and clue cannot be empty");

        var word = new Word(text, clue, category, difficulty);
        
        // Check for duplicates
        if (_words.Values.Any(w => w.Word.Equals(word.Text, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Word '{word.Text}' already exists in dictionary");

        _words[word.Text] = new WordEntry 
        { 
            Word = word.Text, 
            Clue = word.Clue, 
            Category = word.Category, 
            Difficulty = word.Difficulty.ToString() 
        };
    }

    /// <summary>
    /// Creates a new Word instance (helper method for creating test words)
    /// </summary>
    public Word CreateWord(string text, string clue, string category = "", DifficultyLevel difficulty = DifficultyLevel.Medium)
    {
        return new Word(text, clue, category, difficulty);
    }

    /// <summary>
    /// Checks if a word exists in the dictionary
    /// </summary>
    public bool IsValidWord(string word)
    {
        return _words.ContainsKey(word.ToUpperInvariant());
    }
}

/// <summary>
/// Data structure for JSON deserialization
/// </summary>
public class WordEntry
{
    public string Word { get; set; } = string.Empty;
    public string Clue { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Difficulty { get; set; }
}

/// <summary>
/// Dictionary statistics
/// </summary>
public record DictionaryStats
{
    public int TotalWords { get; init; }
    public Dictionary<string, int> Categories { get; init; } = [];
    public Dictionary<int, int> LengthDistribution { get; init; } = [];
    public Dictionary<DifficultyLevel, int> DifficultyDistribution { get; init; } = [];
    public double AverageLength { get; init; }
    public int MinLength { get; init; }
    public int MaxLength { get; init; }
}