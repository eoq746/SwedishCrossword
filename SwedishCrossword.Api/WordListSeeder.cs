using System.Text;

namespace SwedishCrossword.Api;

/// <summary>
/// Seeds and merges the persistent word list directory from the baked-in application data
/// on every boot. Uses a three-way merge so that:
/// - Admin clue edits (in the persistent volume) are preserved across deploys.
/// - New words/clues added in the repo are automatically merged in.
/// - On conflict, admin edits (prod) win.
///
/// A baseline snapshot (stored alongside the persistent files) tracks what was
/// previously deployed, enabling correct diff detection.
/// </summary>
internal static class WordListSeeder
{
    private const string WordListPathEnvVar = "SWEDISH_CROSSWORD_WORDLIST_PATH";
    private const string SeedDataPathEnvVar = "SWEDISH_CROSSWORD_SEED_DATA_PATH";
    private const string BaselineSubdir = ".baseline";

    private static readonly string[] WordListFiles =
    [
        "lexin-words.json",
        "synonym-words.json",
        "kelly-words.json",
        "dsso-words.json",
        "custom-words.json"
    ];

    /// <summary>
    /// On each boot, performs a three-way merge for each word list file:
    /// - Base: the baseline snapshot from the previous deploy (stored in .baseline/)
    /// - Dev: the baked-in seed data from this deploy (/app/Data)
    /// - Prod: the current persistent files (admin edits live here)
    ///
    /// After merging, the baseline is updated to match the current baked-in version.
    /// On first boot (no baseline, no prod), simply copies the seed data.
    /// </summary>
    public static void SeedIfNeeded()
    {
        var targetPath = Environment.GetEnvironmentVariable(WordListPathEnvVar);
        var seedPath = Environment.GetEnvironmentVariable(SeedDataPathEnvVar);

        if (string.IsNullOrWhiteSpace(targetPath) || string.IsNullOrWhiteSpace(seedPath))
            return;

        if (!Directory.Exists(seedPath))
            return;

        Directory.CreateDirectory(targetPath);
        var baselinePath = Path.Combine(targetPath, BaselineSubdir);
        Directory.CreateDirectory(baselinePath);

        foreach (var file in WordListFiles)
        {
            var seedFile = Path.Combine(seedPath, file);
            var prodFile = Path.Combine(targetPath, file);
            var baseFile = Path.Combine(baselinePath, file);

            if (!File.Exists(seedFile))
                continue;

            var tombstonePath = ResolveTombstonePath(targetPath, file);
            var tombstones = WordListMerger.LoadTombstonesFromFile(tombstonePath);
            var devJson = File.ReadAllText(seedFile, Encoding.UTF8);
            var baseJson = File.Exists(baseFile) ? File.ReadAllText(baseFile, Encoding.UTF8) : null;
            var prodJson = File.Exists(prodFile) ? File.ReadAllText(prodFile, Encoding.UTF8) : null;

            if (prodJson is not null && baseJson is null)
                baseJson = prodJson;

            // First boot: no prod file exists, just copy seed
            if (prodJson is null)
            {
                var seededJson = WordListMerger.ApplyTombstones(devJson, tombstones, file);
                WriteJsonAtomic(prodFile, seededJson);
            }
            else
            {
                // Three-way merge: base (previous deploy) vs dev (new deploy) vs prod (admin edits)
                var result = WordListMerger.MergeThreeWay(baseJson, devJson, prodJson, file);
                var mergedJson = WordListMerger.ApplyTombstones(result.MergedJson, tombstones, file);

                // Only write if the merge actually changed something
                if (!string.Equals(mergedJson, prodJson, StringComparison.Ordinal))
                    WriteJsonAtomic(prodFile, mergedJson);
            }

            // Update baseline to current baked-in version
            WriteJsonAtomic(baseFile, devJson);
        }
    }

    private static string ResolveTombstonePath(string targetPath, string wordListFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(wordListFile);

        var tombstoneDirectory = Path.Combine(targetPath, ".meta", "tombstones");
        var tombstoneFile = WordListMerger.GetTombstoneFileName(wordListFile);
        return Path.Combine(tombstoneDirectory, tombstoneFile);
    }

    private static void WriteJsonAtomic(string path, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Target path must include a directory.");

        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, path, overwrite: true);
    }
}
