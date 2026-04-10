using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwedishCrossword.Models;
using System.Text;

namespace SwedishCrossword.Services;

/// <summary>
/// Service for managing Swedish words and their clues
/// </summary>
public class SwedishDictionary
{
    private readonly Dictionary<string, WordEntry> _words;
    private readonly Random _random = new();
    private readonly ILogger<SwedishDictionary> _logger;

    private static bool HasValidClue(WordEntry w) => !string.IsNullOrWhiteSpace(w.Clue) && w.Clue != "___";

    public IReadOnlyList<Word> AllWords => _words.Values.Where(HasValidClue).Select(ConvertToWord).ToList().AsReadOnly();
    public int WordCount => _words.Count;

    public SwedishDictionary(ILogger<SwedishDictionary> logger)
        : this(logger, false)
    {
    }

    public SwedishDictionary()
        : this(NullLogger<SwedishDictionary>.Instance, false)
    {
    }

    public SwedishDictionary(bool empty)
        : this(NullLogger<SwedishDictionary>.Instance, empty)
    {
    }

    public SwedishDictionary(ILogger<SwedishDictionary> logger, bool empty)
    {
        _logger = logger;
        _words = [];

        if (!empty)
        {
            // Try to load Lexin words (if they've been imported)
            var lexinJsonPath = LexinWordImporter.GetJsonFilePath();
            if (File.Exists(lexinJsonPath))
            {
                LoadWordsFromFile(lexinJsonPath);
                _logger.LogInformation("Loaded Lexin dictionary: {WordCount} words", WordCount);
            }
            else
            {
                _logger.LogWarning("Lexin dictionary not found at: {Path}", lexinJsonPath);
                _logger.LogInformation("Run 'Import from Lexin' option to download and import words.");
            }

            // Try to load synonym pair words (if they've been imported)
            var synonymJsonPath = SynonymPairImporter.GetJsonFilePath();
            if (File.Exists(synonymJsonPath))
            {
                var countBefore = WordCount;
                LoadWordsFromFile(synonymJsonPath);
                var synonymsAdded = WordCount - countBefore;
                _logger.LogInformation("Loaded synonym pairs: {SynonymsAdded} additional words", synonymsAdded);
            }

            // Try to load Kelly word list (if it's been imported)
            var kellyJsonPath = KellyWordImporter.GetJsonFilePath();
            if (File.Exists(kellyJsonPath))
            {
                var countBefore = WordCount;
                LoadWordsFromFile(kellyJsonPath);
                var kellyAdded = WordCount - countBefore;
                _logger.LogInformation("Loaded Kelly word list: {KellyAdded} additional words", kellyAdded);
            }

            // Try to load DSSO words (if they've been imported)
            var dssoJsonPath = DssoWordImporter.GetJsonFilePath();
            if (File.Exists(dssoJsonPath))
            {
                var countBefore = WordCount;
                LoadWordsFromFile(dssoJsonPath);
                var dssoAdded = WordCount - countBefore;
                _logger.LogInformation("Loaded DSSO word list: {DssoAdded} additional words", dssoAdded);
            }

            var customJsonPath = Path.Combine(DataDirectory.GetPath(), "custom-words.json");
            if (File.Exists(customJsonPath))
            {
                var countBefore = WordCount;
                LoadWordsFromFile(customJsonPath);
                var customAdded = WordCount - countBefore;
                _logger.LogInformation("Loaded custom word list: {CustomAdded} additional words", customAdded);
            }

        }

        _logger.LogInformation("Total words loaded: {WordCount}", WordCount);
    }

    private void LoadWordsFromFile(string filePath)
    {
        try
        {
            _logger.LogDebug("Loading words from: {FileName}", Path.GetFileName(filePath));

            string jsonText = "";
            Encoding encoding = Encoding.UTF8;

            // Try UTF-8 first
            try
            {
                jsonText = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UTF-8 read failed for {FilePath}", filePath);
            }

            var wordData = JsonSerializer.Deserialize<List<WordEntry>>(jsonText, SafeJsonEncoder.DeserializeOptions);

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
                //Console.WriteLine($"Loaded {wordsAdded} words successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading words from {FilePath}", filePath);
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
        var query = _words.Values.Where(HasValidClue);

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
                HasValidClue(w) &&
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
            .Where(w => HasValidClue(w) && w.Word.Contains(char.ToUpper(letter)))
            .Select(ConvertToWord);
    }

    /// <summary>
    /// Gets random words from the dictionary
    /// </summary>
    public IEnumerable<Word> GetRandomWords(int count, IEnumerable<Word>? excludeWords = null)
    {
        var excludeWordTexts = excludeWords?.Select(w => w.Text.ToUpperInvariant()).ToHashSet() ?? [];
        var availableWords = _words.Values
            .Where(w => HasValidClue(w) && !excludeWordTexts.Contains(w.Word.ToUpperInvariant()))
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
                HasValidClue(w) &&
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
            .Where(HasValidClue)
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
                Categories = [],
                LengthDistribution = [],
                DifficultyDistribution = [],
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
    public static Word CreateWord(string text, string clue, string category = "", DifficultyLevel difficulty = DifficultyLevel.Medium)
    {
        return new Word(text, clue, category, difficulty);
    }

    /// <summary>
    /// Checks if a word exists in the dictionary and has a valid (non-placeholder) clue
    /// </summary>
    public bool IsValidWord(string word)
    {
        return _words.TryGetValue(word.ToUpperInvariant(), out var entry) && HasValidClue(entry);
    }

    /// <summary>
    /// Gets the clue for a word if it exists in the dictionary and has a valid clue.
    /// O(1) dictionary lookup — avoids materializing the full AllWords list.
    /// </summary>
    public string? GetClue(string word)
    {
        if (_words.TryGetValue(word.ToUpperInvariant(), out var entry) && HasValidClue(entry))
            return entry.Clue;
        return null;
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