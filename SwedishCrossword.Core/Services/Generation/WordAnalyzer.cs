namespace SwedishCrossword.Services.Generation;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwedishCrossword.Models;

/// <summary>
/// Analyzes word connectivity and caches results to disk for reuse across runs.
/// </summary>
internal class WordAnalyzer
{
    private readonly Lock _analysisCacheLock = new();
    private string? _cachedWordsFingerprint;
    private List<WordAnalysis>? _cachedWordAnalysis;

    private const string CacheFileName = "wordAnalysisCache.json";

    private record WordAnalysisDto(string Text, double ConnectivityScore, int VowelCount, int CommonLetterCount);
    private record CacheFilePayload(string Fingerprint, List<WordAnalysisDto> Entries);

    public List<WordAnalysis> AnalyzeWordConnectivity(List<Word> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var fingerprint = ComputeWordsFingerprint(words);

        lock (_analysisCacheLock)
        {
            if (fingerprint == _cachedWordsFingerprint && _cachedWordAnalysis != null)
            {
                return [.._cachedWordAnalysis];
            }

            var disk = LoadAnalysisFromDisk(fingerprint, words);
            if (disk != null)
            {
                _cachedWordsFingerprint = fingerprint;
                _cachedWordAnalysis = [..disk];
                return [.._cachedWordAnalysis];
            }

            var letterWordCount = new Dictionary<char, int>();
            foreach (var word in words)
            {
                var seen = new HashSet<char>();
                foreach (var c in word.Text)
                {
                    if (seen.Add(c))
                    {
                        letterWordCount[c] = letterWordCount.GetValueOrDefault(c, 0) + 1;
                    }
                }
            }

            var analysis = new List<WordAnalysis>(words.Count);
            foreach (var word in words)
            {
                var (connectivityScore, vowelCount, commonLetterCount) = CalculateConnectivityScore(word, letterWordCount);
                analysis.Add(new WordAnalysis
                {
                    Word = word,
                    ConnectivityScore = connectivityScore,
                    VowelCount = vowelCount,
                    CommonLetterCount = commonLetterCount
                });
            }

            _cachedWordsFingerprint = fingerprint;
            _cachedWordAnalysis = [..analysis];

            SaveAnalysisToDisk(fingerprint, _cachedWordAnalysis);

            return analysis;
        }
    }

    private static string ComputeWordsFingerprint(List<Word> words)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var sortedTexts = words.ConvertAll(w => w.Text);
        sortedTexts.Sort(StringComparer.Ordinal);

        foreach (var text in sortedTexts)
        {
            var lengthPrefix = Encoding.UTF8.GetBytes($"{text.Length}:");
            sha256.AppendData(lengthPrefix);
            sha256.AppendData(Encoding.UTF8.GetBytes(text));
        }

        var hashBytes = sha256.GetHashAndReset();
        return Convert.ToHexStringLower(hashBytes);
    }

    internal static (double Score, int VowelCount, int CommonLetterCount) CalculateConnectivityScore(
        Word targetWord, Dictionary<char, int> letterWordCount)
    {
        var score = 0.0;
        var letterFreq = new Dictionary<char, int>();
        int vowelCount = 0;
        int commonLetterCount = 0;

        foreach (var c in targetWord.Text)
        {
            letterFreq[c] = letterFreq.GetValueOrDefault(c, 0) + 1;

            if (c is 'A' or 'E' or 'I' or 'O' or 'U' or 'Å' or 'Ä' or 'Ö')
            {
                vowelCount++;
                commonLetterCount++;

                if (c is 'A' or 'E' or 'I' or 'O' or 'U')
                    score += 0.3;
                else
                    score += 0.2;
            }
            else if (c is 'R' or 'N' or 'S' or 'T' or 'L' or 'K')
            {
                commonLetterCount++;
                if (c is 'R' or 'N' or 'S' or 'T' or 'L')
                    score += 0.5;
            }
        }

        foreach (var kvp in letterFreq)
        {
            if (letterWordCount.TryGetValue(kvp.Key, out var wordCount))
            {
                var otherWordCount = wordCount - 1;
                if (otherWordCount > 0)
                {
                    score += otherWordCount * (kvp.Value / Math.Sqrt(kvp.Value));
                }
            }
        }

        if (targetWord.Length > 0)
            score /= targetWord.Length;

        if (targetWord.Length >= 15) score *= 0.05;
        if (targetWord.Length >= 16) score *= 0.05;

        return (score, vowelCount, commonLetterCount);
    }

    private string GetCacheDirectory()
    {
        var env = Environment.GetEnvironmentVariable("SWEDISH_CROSSWORD_CACHE_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                if (env.StartsWith('~'))
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    env = Path.Combine(home, env.TrimStart('~').TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }

                return Path.GetFullPath(env);
            }
            catch
            {
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwedishCrossword");
    }

    private string GetCacheFilePath()
    {
        var dir = GetCacheDirectory();
        return Path.Combine(dir, CacheFileName);
    }

    private List<WordAnalysis>? LoadAnalysisFromDisk(string fingerprint, List<Word> words)
    {
        try
        {
            var filePath = GetCacheFilePath();
            if (!File.Exists(filePath)) return null;

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<CacheFilePayload>(json);
            if (payload == null) return null;
            if (payload.Fingerprint != fingerprint) return null;

            if (payload.Entries.Count != words.Count) return null;

            var wordMap = words.ToDictionary(w => w.Text, StringComparer.OrdinalIgnoreCase);
            var result = new List<WordAnalysis>(payload.Entries.Count);
            foreach (var dto in payload.Entries)
            {
                if (!wordMap.TryGetValue(dto.Text, out var word))
                {
                    return null;
                }

                result.Add(new WordAnalysis
                {
                    Word = word,
                    ConnectivityScore = dto.ConnectivityScore,
                    VowelCount = dto.VowelCount,
                    CommonLetterCount = dto.CommonLetterCount
                });
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private void SaveAnalysisToDisk(string fingerprint, List<WordAnalysis> analysis)
    {
        try
        {
            var dir = GetCacheDirectory();
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, CacheFileName);

            var dtos = analysis.ConvertAll(a => new WordAnalysisDto(a.Word.Text, a.ConnectivityScore, a.VowelCount, a.CommonLetterCount));
            var payload = new CacheFilePayload(fingerprint, dtos);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore disk errors - caching should be best-effort
        }
    }
}
