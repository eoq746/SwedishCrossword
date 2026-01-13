using SwedishCrossword.Models;

namespace SwedishCrossword.Services;

/// <summary>
/// Service for generating and managing Swedish crossword clues
/// </summary>
public class ClueGenerator
{
    private readonly Random _random = new();
    private readonly Dictionary<string, List<string>> _clueTemplates = [];

    public ClueGenerator()
    {
        InitializeSwedishClueTemplates();
    }

    /// <summary>
    /// Initialize Swedish clue templates for different categories
    /// </summary>
    private void InitializeSwedishClueTemplates()
    {
        _clueTemplates["Djur"] = [
            "{0} som lever i {1}",
            "Fyrbent {0}",
            "Vilt {0}",
            "{0} med {1}",
            "Husdjur av typ {0}"
        ];

        _clueTemplates["Natur"] = [
            "Naturens {0}",
            "{0} i naturen",
            "Växer som {0}",
            "Finns i {0}",
            "Del av {0}"
        ];

        _clueTemplates["Mat"] = [
            "Maträtt med {0}",
            "Ätbar {0}",
            "Smakrik {0}",
            "{0} att äta",
            "Näring från {0}"
        ];

        _clueTemplates["Föremål"] = [
            "Använder man {0}",
            "Praktisk {0}",
            "Vardagsföremål för {0}",
            "Redskap för {0}",
            "Hjälpmedel vid {0}"
        ];

        _clueTemplates["Färg"] = [
            "Färg som {0}",
            "Nyans av {0}",
            "Kulör likt {0}",
            "Ton som påminner om {0}"
        ];

        _clueTemplates["Geografi"] = [
            "Plats i {0}",
            "Stad i {0}",
            "Land vid {0}",
            "Ort känd för {0}",
            "Region med {0}"
        ];

        _clueTemplates["Verb"] = [
            "Att {0}",
            "Handlingen att {0}",
            "Aktivitet som innebär {0}",
            "Gör när man {0}"
        ];

        _clueTemplates["Yrken"] = [
            "Person som {0}",
            "Arbetar med {0}",
            "Yrkesutövare inom {0}",
            "Jobbar som {0}",
            "Sysslar med {0}"
        ];

        _clueTemplates["Sport"] = [
            "Sport med {0}",
            "Idrottsgren där man {0}",
            "Tävlingssport med {0}",
            "Aktivitet som kräver {0}"
        ];

        _clueTemplates["Känslor"] = [
            "Känsla av {0}",
            "Emotion som {0}",
            "Sinnesstämning präglad av {0}",
            "Upplevelse av {0}"
        ];

        _clueTemplates["Årstid"] = [
            "Tid på året när {0}",
            "Period präglad av {0}",
            "Årstid med {0}",
            "Månader när {0}"
        ];

        _clueTemplates["Veckodagar"] = [
            "Dag nummer {0} i veckan",
            "Veckodag efter {0}",
            "Dag när {0}",
            "Kallas även {0}"
        ];
    }

    /// <summary>
    /// Generates alternative clues for a word based on its category
    /// </summary>
    public IEnumerable<string> GenerateAlternativeClues(Word word, int maxClues = 3)
    {
        var clues = new List<string>();

        // Always include the original clue
        clues.Add(word.Clue);

        // Try to generate template-based clues
        if (!string.IsNullOrEmpty(word.Category) && _clueTemplates.ContainsKey(word.Category))
        {
            var templates = _clueTemplates[word.Category];
            var shuffledTemplates = templates.OrderBy(x => _random.Next()).Take(maxClues - 1);

            foreach (var template in shuffledTemplates)
            {
                var alternativeClue = GenerateClueFromTemplate(template, word);
                if (!string.IsNullOrEmpty(alternativeClue) && !clues.Contains(alternativeClue))
                {
                    clues.Add(alternativeClue);
                }
            }
        }

        // Generate difficulty-based clues
        clues.AddRange(GenerateDifficultyBasedClues(word, maxClues - clues.Count));

        return clues.Take(maxClues);
    }

    private string GenerateClueFromTemplate(string template, Word word)
    {
        // This is a simplified implementation
        // In a real system, you might have more sophisticated template processing
        try
        {
            return string.Format(template, GetWordFeatures(word));
        }
        catch
        {
            return string.Empty;
        }
    }

    private object[] GetWordFeatures(Word word)
    {
        // Extract features from the word for template substitution
        var features = new List<object> { word.Text };

        switch (word.Category.ToLower())
        {
            case "djur":
                features.Add(GetAnimalHabitat(word.Text));
                features.Add(GetAnimalFeature(word.Text));
                break;
            case "färg":
                features.Add(GetColorReference(word.Text));
                break;
            case "geografi":
                features.Add(GetGeographicContext(word.Text));
                break;
            default:
                features.Add("okänd");
                break;
        }

        return features.ToArray();
    }

    private string GetAnimalHabitat(string animal)
    {
        return animal.ToUpper() switch
        {
            "KATT" => "hemmet",
            "HUND" => "hemmet",
            "FISK" => "vattnet",
            "FÅGEL" => "luften",
            "HÄST" => "stallet",
            _ => "naturen"
        };
    }

    private string GetAnimalFeature(string animal)
    {
        return animal.ToUpper() switch
        {
            "KATT" => "whiskers",
            "HUND" => "svans",
            "FISK" => "fjäll",
            "FÅGEL" => "vingar",
            "HÄST" => "man",
            _ => "hår"
        };
    }

    private string GetColorReference(string color)
    {
        return color.ToUpper() switch
        {
            "RÖD" => "blod",
            "BLÅ" => "himlen",
            "GRÖN" => "gräset",
            "GUL" => "solen",
            "SVART" => "natten",
            "VIT" => "snön",
            _ => "något"
        };
    }

    private string GetGeographicContext(string place)
    {
        return place.ToUpper() switch
        {
            "STOCKHOLM" => "Sverige",
            "GÖTEBORG" => "Västkusten",
            "MALMÖ" => "Skåne",
            "SVERIGE" => "Skandinavien",
            "NORGE" => "Skandinavien",
            "FINLAND" => "Norden",
            "DANMARK" => "Skandinavien",
            _ => "världen"
        };
    }

    /// <summary>
    /// Generates clues based on difficulty level
    /// </summary>
    private IEnumerable<string> GenerateDifficultyBasedClues(Word word, int maxClues)
    {
        var clues = new List<string>();
        
        if (maxClues <= 0) return clues;

        switch (word.Difficulty)
        {
            case DifficultyLevel.Easy:
                clues.AddRange(GenerateEasyClues(word, maxClues));
                break;
            case DifficultyLevel.Medium:
                clues.AddRange(GenerateMediumClues(word, maxClues));
                break;
            case DifficultyLevel.Hard:
                clues.AddRange(GenerateHardClues(word, maxClues));
                break;
        }

        return clues.Take(maxClues);
    }

    private IEnumerable<string> GenerateEasyClues(Word word, int maxClues)
    {
        var clues = new List<string>();

        // Length-based clue
        if (clues.Count < maxClues)
        {
            clues.Add($"Ord med {word.Length} bokstäver");
        }

        // First letter clue
        if (clues.Count < maxClues)
        {
            clues.Add($"Börjar på {word.Text.First()}");
        }

        return clues;
    }

    private IEnumerable<string> GenerateMediumClues(Word word, int maxClues)
    {
        var clues = new List<string>();

        // Rhyme-based clues (simplified)
        if (clues.Count < maxClues)
        {
            var rhymeWord = FindSimpleRhyme(word.Text);
            if (!string.IsNullOrEmpty(rhymeWord))
            {
                clues.Add($"Rimmar på {rhymeWord}");
            }
        }

        // Letter pattern clue
        if (clues.Count < maxClues && word.Length >= 4)
        {
            var pattern = CreateLetterPattern(word.Text);
            clues.Add($"Mönster: {pattern}");
        }

        return clues;
    }

    private IEnumerable<string> GenerateHardClues(Word word, int maxClues)
    {
        var clues = new List<string>();

        // Anagram clue
        if (clues.Count < maxClues && word.Length >= 4)
        {
            var anagram = CreateSimpleAnagram(word.Text);
            if (!string.IsNullOrEmpty(anagram))
            {
                clues.Add($"Anagram av {anagram}");
            }
        }

        // Cryptic-style clue
        if (clues.Count < maxClues)
        {
            var crypticClue = GenerateCrypticClue(word);
            if (!string.IsNullOrEmpty(crypticClue))
            {
                clues.Add(crypticClue);
            }
        }

        return clues;
    }

    private string FindSimpleRhyme(string word)
    {
        // Simplified Swedish rhyming - just check common endings
        var ending = word.Length >= 2 ? word[^2..] : word;
        
        return ending.ToUpper() switch
        {
            "AT" => "katt",
            "US" => "hus", 
            "OL" => "sol",
            "ÖD" => "röd",
            _ => string.Empty
        };
    }

    private string CreateLetterPattern(string word)
    {
        // Create a pattern like "_ A _ _" for gaps
        var pattern = new List<string>();
        for (int i = 0; i < word.Length; i++)
        {
            if (i % 2 == 1) // Show every other letter
            {
                pattern.Add(word[i].ToString());
            }
            else
            {
                pattern.Add("_");
            }
        }
        return string.Join(" ", pattern);
    }

    private string CreateSimpleAnagram(string word)
    {
        // Create a simple anagram by shuffling letters
        var letters = word.ToCharArray();
        for (int i = 0; i < letters.Length; i++)
        {
            var swapIndex = _random.Next(letters.Length);
            (letters[i], letters[swapIndex]) = (letters[swapIndex], letters[i]);
        }
        
        var anagram = new string(letters);
        return anagram == word ? string.Empty : anagram; // Don't return if it's the same
    }

    private string GenerateCrypticClue(Word word)
    {
        // Very simplified cryptic clue generation
        var templates = new[]
        {
            $"Blandat {CreateSimpleAnagram(word.Text)} blir {word.Category.ToLower()}",
            $"Utan {word.Text.First()} blir det {word.Category.ToLower()}",
            $"Del av '{CreateHiddenWord(word.Text)}' är {word.Category.ToLower()}"
        };

        return templates[_random.Next(templates.Length)];
    }

    private string CreateHiddenWord(string word)
    {
        // Create a phrase where the word is hidden
        var prefixes = new[] { "be", "av", "ut", "in", "på" };
        var suffixes = new[] { "ning", "are", "het", "dom", "skap" };
        
        var prefix = prefixes[_random.Next(prefixes.Length)];
        var suffix = suffixes[_random.Next(suffixes.Length)];
        
        return $"{prefix}{word.ToLower()}{suffix}";
    }

    /// <summary>
    /// Adjusts clue difficulty for a word
    /// </summary>
    public string AdjustClueForDifficulty(Word word, DifficultyLevel targetDifficulty)
    {
        if (word.Difficulty == targetDifficulty)
        {
            return word.Clue;
        }

        var alternativeClues = GenerateAlternativeClues(word, 5).ToList();
        
        // Try to find a clue that matches the target difficulty
        foreach (var clue in alternativeClues)
        {
            if (EstimateClueComplexity(clue) == targetDifficulty)
            {
                return clue;
            }
        }

        // If no perfect match, return the original or first alternative
        return alternativeClues.FirstOrDefault() ?? word.Clue;
    }

    private DifficultyLevel EstimateClueComplexity(string clue)
    {
        var complexity = 0;

        // Word count contributes to complexity
        var wordCount = clue.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        complexity += wordCount * 2;

        // Certain words indicate higher difficulty
        var hardWords = new[] { "anagram", "mönster", "rimmar", "blandat", "del av" };
        foreach (var hardWord in hardWords)
        {
            if (clue.ToLower().Contains(hardWord))
            {
                complexity += 10;
            }
        }

        // Numbers and symbols increase complexity
        if (clue.Any(char.IsDigit) || clue.Contains("_"))
        {
            complexity += 5;
        }

        return complexity switch
        {
            < 10 => DifficultyLevel.Easy,
            < 20 => DifficultyLevel.Medium,
            _ => DifficultyLevel.Hard
        };
    }

    /// <summary>
    /// Validates that a clue is appropriate for its word
    /// </summary>
    public bool IsValidClue(Word word, string clue)
    {
        if (string.IsNullOrWhiteSpace(clue))
            return false;

        // Clue shouldn't contain the answer
        var clueWords = clue.ToUpper().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (clueWords.Contains(word.Text.ToUpper()))
            return false;

        // Clue shouldn't be too short or too long
        return clue.Length >= 5 && clue.Length <= 100;
    }
}