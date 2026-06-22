using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwedishCrossword.Services;

namespace SwedishCrossword.Api;

internal enum WordListUpdateResult
{
    Updated,
    NotFound,
    VersionConflict
}

internal sealed record WordListUpdateResponse(WordListUpdateResult Result, string CurrentVersion, string? SourceKey = null);
internal sealed record WordListTombstoneSnapshot(string SourceKey, string FileName, int Count, string Version, List<string>? Words = null);

internal sealed class WordListAdminService
{
    private readonly Lock _writeLock = new();

    private static readonly JsonSerializerOptions JsonOptions = SafeJsonEncoder.DefaultOptions;

    private static readonly (string SourceKey, Func<string> ResolvePath)[] SourceFiles =
    [
        ("lexin", LexinWordImporter.GetJsonFilePath),
        ("synonyms", SynonymPairImporter.GetJsonFilePath),
        ("kelly", KellyWordImporter.GetJsonFilePath),
        ("dsso", DssoWordImporter.GetJsonFilePath),
        ("custom", () => Path.Combine(DataDirectory.GetPath(), "custom-words.json"))
    ];

    public WordListUpdateResponse UpdateClueInOriginFile(
        string word,
        string currentClue,
        string? updatedClue,
        string? expectedVersion,
        bool removeClue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentClue);
        if (!removeClue)
            ArgumentException.ThrowIfNullOrWhiteSpace(updatedClue);

        lock (_writeLock)
        {
            var resolved = ResolveWordOrigin(word, currentClue);
            if (resolved is null)
                return new WordListUpdateResponse(WordListUpdateResult.NotFound, string.Empty);

            var (sourceKey, filePath, _, matchAlternative) = resolved.Value;
            var currentVersion = GetFileVersion(filePath);

            if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                !string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            {
                return new WordListUpdateResponse(WordListUpdateResult.VersionConflict, currentVersion, sourceKey);
            }

            var entries = LoadWordEntries(filePath);
            var entryIndex = entries.FindIndex(e => e.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (entryIndex < 0)
                return new WordListUpdateResponse(WordListUpdateResult.NotFound, currentVersion, sourceKey);

            var entry = entries[entryIndex];
            var normalizedWord = WordListMerger.NormalizeWord(entry.Word);
            var tombstonePath = ResolveTombstonePath(filePath);
            var tombstones = LoadTombstones(tombstonePath);

            if (removeClue)
            {
                RemoveClue(entry, currentClue);

                if (string.IsNullOrWhiteSpace(entry.Clue))
                {
                    entries.RemoveAt(entryIndex);
                    tombstones.Add(normalizedWord);
                }
            }
            else
            {
                var normalizedUpdated = updatedClue!.Trim();
                if (matchAlternative)
                {
                    var altIndex = entry.AlternativeClues.FindIndex(c => string.Equals(c, currentClue, StringComparison.Ordinal));
                    if (altIndex >= 0)
                        entry.AlternativeClues[altIndex] = normalizedUpdated;
                    else
                        entry.Clue = normalizedUpdated;
                }
                else
                {
                    entry.Clue = normalizedUpdated;
                }
            }

            WriteWordEntries(filePath, entries);
            WriteTombstones(tombstonePath, tombstones);
            return new WordListUpdateResponse(WordListUpdateResult.Updated, GetFileVersion(filePath), sourceKey);
        }
    }

    public WordListUpdateResponse AddCustomWordEntry(
        string word,
        string clue,
        string? category,
        string? difficulty,
        string? expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        ArgumentException.ThrowIfNullOrWhiteSpace(clue);

        lock (_writeLock)
        {
            var customPath = Path.Combine(DataDirectory.GetPath(), "custom-words.json");
            var currentVersion = GetFileVersion(customPath);

            if (!string.IsNullOrWhiteSpace(expectedVersion) &&
                !string.Equals(expectedVersion, currentVersion, StringComparison.Ordinal))
            {
                return new WordListUpdateResponse(WordListUpdateResult.VersionConflict, currentVersion, "custom");
            }

            var entries = LoadWordEntries(customPath);
            var normalizedWord = WordListMerger.NormalizeWord(word);
            var normalizedClue = clue.Trim();
            var existing = entries.FirstOrDefault(e => e.Word.Equals(normalizedWord, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (string.Equals(existing.Clue, normalizedClue, StringComparison.Ordinal) ||
                    existing.AlternativeClues.Any(c => string.Equals(c, normalizedClue, StringComparison.Ordinal)))
                {
                    return new WordListUpdateResponse(WordListUpdateResult.VersionConflict, currentVersion, "custom");
                }

                existing.AlternativeClues.Add(normalizedClue);
            }
            else
            {
                entries.Add(new WordEntry
                {
                    Word = normalizedWord,
                    Clue = normalizedClue,
                    Category = string.IsNullOrWhiteSpace(category) ? "Custom" : category.Trim(),
                    Difficulty = string.IsNullOrWhiteSpace(difficulty) ? "Medium" : difficulty.Trim()
                });
            }

            WriteWordEntries(customPath, entries);

            var tombstonePath = ResolveTombstonePath(customPath);
            var tombstones = LoadTombstones(tombstonePath);
            if (tombstones.Remove(normalizedWord))
                WriteTombstones(tombstonePath, tombstones);

            return new WordListUpdateResponse(WordListUpdateResult.Updated, GetFileVersion(customPath), "custom");
        }
    }

    /// <summary>
    /// Returns persisted tombstone diagnostics per source word list file.
    /// </summary>
    public List<WordListTombstoneSnapshot> GetTombstoneSnapshots(bool includeWords)
    {
        lock (_writeLock)
        {
            var snapshots = new List<WordListTombstoneSnapshot>(SourceFiles.Length);
            foreach (var (sourceKey, resolvePath) in SourceFiles)
            {
                var sourcePath = resolvePath();
                var tombstonePath = ResolveTombstonePath(sourcePath);
                var tombstones = LoadTombstones(tombstonePath);
                var words = includeWords
                    ? tombstones.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList()
                    : null;

                snapshots.Add(new WordListTombstoneSnapshot(
                    sourceKey,
                    Path.GetFileName(sourcePath),
                    tombstones.Count,
                    GetFileVersion(tombstonePath),
                    words));
            }

            return snapshots;
        }
    }

    private static void RemoveClue(WordEntry entry, string currentClue)
    {
        if (string.Equals(entry.Clue, currentClue, StringComparison.Ordinal))
        {
            if (entry.AlternativeClues.Count > 0)
            {
                entry.Clue = entry.AlternativeClues[0];
                entry.AlternativeClues.RemoveAt(0);
                return;
            }

            entry.Clue = string.Empty;
            return;
        }

        entry.AlternativeClues.RemoveAll(c => string.Equals(c, currentClue, StringComparison.Ordinal));
    }

    private static (string SourceKey, string FilePath, WordEntry Entry, bool MatchAlternative)? ResolveWordOrigin(string word, string currentClue)
    {
        foreach (var (sourceKey, resolvePath) in SourceFiles)
        {
            var filePath = resolvePath();
            if (!File.Exists(filePath))
                continue;

            var entries = LoadWordEntries(filePath);
            var entry = entries.FirstOrDefault(e => e.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;

            if (string.Equals(entry.Clue, currentClue, StringComparison.Ordinal))
                return (sourceKey, filePath, entry, false);

            if (entry.AlternativeClues.Any(c => string.Equals(c, currentClue, StringComparison.Ordinal)))
                return (sourceKey, filePath, entry, true);
        }

        return null;
    }

    private static string GetFileVersion(string filePath)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        var bytes = File.ReadAllBytes(filePath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    private static List<WordEntry> LoadWordEntries(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<List<WordEntry>>(json, SafeJsonEncoder.DeserializeOptions) ?? [];
    }

    private static void WriteWordEntries(string filePath, List<WordEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var json = JsonSerializer.Serialize(entries, JsonOptions);
        var tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, filePath, overwrite: true);
    }

    private static string ResolveTombstonePath(string sourceFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        var dataPath = DataDirectory.GetPath();
        var tombstoneDirectory = Path.Combine(dataPath, ".meta", "tombstones");
        var tombstoneFileName = WordListMerger.GetTombstoneFileName(Path.GetFileName(sourceFilePath));
        return Path.Combine(tombstoneDirectory, tombstoneFileName);
    }

    private static HashSet<string> LoadTombstones(string tombstonePath)
        => WordListMerger.LoadTombstonesFromFile(tombstonePath);

    private static void WriteTombstones(string tombstonePath, HashSet<string> tombstones)
        => WordListMerger.WriteTombstonesToFile(tombstonePath, tombstones);
}
