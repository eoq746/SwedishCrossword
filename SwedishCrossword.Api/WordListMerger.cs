using System.Text;
using System.Text.Json;
using SwedishCrossword.Services;

namespace SwedishCrossword.Api;

/// <summary>
/// Three-way merge logic for word list JSON files.
/// Shared between <see cref="BlobWordListSyncService"/> (blob sync) and
/// <see cref="WordListSeeder"/> (local startup merge).
/// </summary>
internal static class WordListMerger
{
    private static readonly JsonSerializerOptions JsonOptions = SafeJsonEncoder.DefaultOptions;

    public sealed record MergeResult(string MergedJson, int Added, int Updated, int Removed, int Conflicts, List<ConflictDetail> ConflictDetails);
    public sealed record ConflictDetail(string Word, string Reason, string Resolution);

    /// <summary>
    /// Performs a three-way merge of word list JSON content.
    /// Base = common ancestor, dev = incoming changes, prod = current state (admin edits).
    /// On conflict, prod wins.
    /// </summary>
    public static MergeResult MergeThreeWay(string? baseJson, string? devJson, string? prodJson, string fileName)
    {
        var baseMap = ParseWordMap(baseJson, fileName);
        var devMap = ParseWordMap(devJson, fileName);
        var prodMap = ParseWordMap(prodJson, fileName);

        var merged = new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);
        var keys = baseMap.Keys
            .Union(devMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(prodMap.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var added = 0;
        var updated = 0;
        var removed = 0;
        var conflicts = 0;
        var conflictDetails = new List<ConflictDetail>();

        foreach (var key in keys)
        {
            baseMap.TryGetValue(key, out var baseEntry);
            devMap.TryGetValue(key, out var devEntry);
            prodMap.TryGetValue(key, out var prodEntry);

            var devChanged = !EntryEquals(baseEntry, devEntry);
            var prodChanged = !EntryEquals(baseEntry, prodEntry);

            if (!devChanged)
            {
                if (prodEntry is not null)
                    merged[key] = CloneEntry(prodEntry);
                continue;
            }

            if (!prodChanged)
            {
                if (devEntry is not null)
                    merged[key] = CloneEntry(devEntry);
                continue;
            }

            var baseExists = baseEntry is not null;
            var devExists = devEntry is not null;
            var prodExists = prodEntry is not null;

            if (!baseExists)
            {
                if (devExists && !prodExists)
                {
                    merged[key] = CloneEntry(devEntry!);
                    added++;
                    continue;
                }

                if (!devExists && prodExists)
                {
                    merged[key] = CloneEntry(prodEntry!);
                    continue;
                }

                if (devExists && prodExists && EntryEquals(devEntry, prodEntry))
                {
                    merged[key] = CloneEntry(devEntry!);
                    added++;
                    continue;
                }

                conflicts++;
                conflictDetails.Add(new ConflictDetail(key, "Concurrent add mismatch", "kept-prod"));
                if (prodEntry is not null)
                    merged[key] = CloneEntry(prodEntry);
                continue;
            }

            if (baseExists && !devExists && !prodExists)
            {
                removed++;
                continue;
            }

            if (baseExists && !devExists && prodExists)
            {
                conflicts++;
                conflictDetails.Add(new ConflictDetail(key, "Deleted in dev but changed in prod", "kept-prod"));
                merged[key] = CloneEntry(prodEntry!);
                continue;
            }

            if (baseExists && devExists && !prodExists)
            {
                conflicts++;
                conflictDetails.Add(new ConflictDetail(key, "Deleted in prod but changed in dev", "kept-prod-delete"));
                continue;
            }

            var mergedEntry = MergeEntry(baseEntry!, devEntry!, prodEntry!, out var entryConflict, out var entryUpdated);
            if (entryConflict)
            {
                conflicts++;
                conflictDetails.Add(new ConflictDetail(key, "Field-level mismatch", "kept-prod-field"));
            }
            if (entryUpdated)
                updated++;

            merged[key] = mergedEntry;
        }

        var ordered = merged.Values
            .OrderBy(e => e.Word, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mergedJson = JsonSerializer.Serialize(ordered, JsonOptions);

        return new MergeResult(mergedJson, added, updated, removed, conflicts, conflictDetails);
    }

    public static Dictionary<string, WordEntry> ParseWordMap(string? json, string fileName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var entries = JsonSerializer.Deserialize<List<WordEntry>>(json, SafeJsonEncoder.DeserializeOptions) ?? [];
            var map = new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Word))
                    continue;
                map[entry.Word.ToUpperInvariant()] = Normalize(entry);
            }
            return map;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid word list JSON in {fileName}: {ex.Message}", ex);
        }
    }

    private static WordEntry MergeEntry(WordEntry @base, WordEntry dev, WordEntry prod, out bool conflict, out bool updated)
    {
        conflict = false;

        var clue = MergeScalar(@base.Clue, dev.Clue, prod.Clue, ref conflict);
        var category = MergeScalar(@base.Category, dev.Category, prod.Category, ref conflict);
        var difficulty = MergeScalar(@base.Difficulty, dev.Difficulty, prod.Difficulty, ref conflict);
        var alternatives = MergeAlternatives(@base.AlternativeClues, dev.AlternativeClues, prod.AlternativeClues, ref conflict);

        var merged = new WordEntry
        {
            Word = prod.Word,
            Clue = clue ?? string.Empty,
            Category = category,
            Difficulty = difficulty,
            AlternativeClues = alternatives
        };

        updated = !EntryEquals(@base, merged);
        return merged;
    }

    private static string? MergeScalar(string? @base, string? dev, string? prod, ref bool conflict)
    {
        if (string.Equals(dev, @base, StringComparison.Ordinal))
            return prod;
        if (string.Equals(prod, @base, StringComparison.Ordinal))
            return dev;
        if (string.Equals(dev, prod, StringComparison.Ordinal))
            return dev;

        conflict = true;
        return prod;
    }

    private static List<string> MergeAlternatives(List<string> baseList, List<string> devList, List<string> prodList, ref bool conflict)
    {
        if (SequenceEquals(devList, baseList))
            return [.. prodList];
        if (SequenceEquals(prodList, baseList))
            return [.. devList];
        if (SequenceEquals(devList, prodList))
            return [.. devList];

        conflict = true;
        return [.. prodList];
    }

    private static bool SequenceEquals(List<string> left, List<string> right)
        => left.SequenceEqual(right, StringComparer.Ordinal);

    public static bool EntryEquals(WordEntry? left, WordEntry? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(left.Word, right.Word, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Clue, right.Clue, StringComparison.Ordinal)
            && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            && string.Equals(left.Difficulty, right.Difficulty, StringComparison.Ordinal)
            && SequenceEquals(left.AlternativeClues, right.AlternativeClues);
    }

    private static WordEntry CloneEntry(WordEntry source) => new()
    {
        Word = source.Word,
        Clue = source.Clue,
        Category = source.Category,
        Difficulty = source.Difficulty,
        AlternativeClues = [.. source.AlternativeClues]
    };

    private static WordEntry Normalize(WordEntry entry)
    {
        entry.Word = entry.Word.Trim().ToUpperInvariant();
        entry.Clue = entry.Clue?.Trim() ?? string.Empty;
        entry.Category = string.IsNullOrWhiteSpace(entry.Category) ? null : entry.Category.Trim();
        entry.Difficulty = string.IsNullOrWhiteSpace(entry.Difficulty) ? null : entry.Difficulty.Trim();
        entry.AlternativeClues = [.. entry.AlternativeClues
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)];
        return entry;
    }

    public static string GetTombstoneFileName(string wordListFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wordListFileName);
        return $"{Path.GetFileNameWithoutExtension(wordListFileName)}-tombstones.json";
    }

    public static HashSet<string> LoadTombstonesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var words = JsonSerializer.Deserialize<List<string>>(json, SafeJsonEncoder.DeserializeOptions) ?? [];
            return new HashSet<string>(words
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(NormalizeWord), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid tombstone JSON: {ex.Message}", ex);
        }
    }

    public static string SerializeTombstones(HashSet<string> tombstones)
    {
        ArgumentNullException.ThrowIfNull(tombstones);

        var ordered = tombstones
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(NormalizeWord)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return JsonSerializer.Serialize(ordered, JsonOptions);
    }

    public static string ApplyTombstones(string mergedJson, IReadOnlySet<string> tombstones, string fileName)
    {
        ArgumentNullException.ThrowIfNull(mergedJson);
        ArgumentNullException.ThrowIfNull(tombstones);

        if (tombstones.Count == 0)
            return mergedJson;

        var map = ParseWordMap(mergedJson, fileName);
        foreach (var tombstone in tombstones)
        {
            if (string.IsNullOrWhiteSpace(tombstone))
                continue;

            map.Remove(NormalizeWord(tombstone));
        }

        var ordered = map.Values
            .OrderBy(e => e.Word, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return JsonSerializer.Serialize(ordered, JsonOptions);
    }

    public static HashSet<string> LoadTombstonesFromFile(string tombstoneFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tombstoneFilePath);

        if (!File.Exists(tombstoneFilePath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var json = File.ReadAllText(tombstoneFilePath, Encoding.UTF8);
        return LoadTombstonesFromJson(json);
    }

    public static void WriteTombstonesToFile(string tombstoneFilePath, HashSet<string> tombstones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tombstoneFilePath);
        ArgumentNullException.ThrowIfNull(tombstones);

        var directory = Path.GetDirectoryName(tombstoneFilePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Tombstone file path must include a directory.");

        Directory.CreateDirectory(directory);

        var json = SerializeTombstones(tombstones);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(tombstoneFilePath)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, tombstoneFilePath, overwrite: true);
    }

    public static string NormalizeWord(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return word.Trim().ToUpperInvariant();
    }
}
