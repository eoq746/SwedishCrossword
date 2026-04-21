using System.Globalization;
using System.Collections.Concurrent;

namespace SwedishCrossword.Api;

/// <summary>
/// Thread-safe in-memory index of available puzzle dates and their sizes.
/// Populated by <see cref="PuzzleWarmupService"/> and queried by the dates endpoint,
/// avoiding filesystem scans on every request.
/// </summary>
sealed class PuzzleDateIndex
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _dateSizes = new();
    private readonly string _puzzlePath;
    private volatile bool _initialScanDone;

    public PuzzleDateIndex(IConfiguration config)
    {
        var path = config["Storage:PuzzlePath"];
        _puzzlePath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "puzzles")
            : path;
    }

    /// <summary>
    /// Records that a puzzle exists for the given date and size.
    /// </summary>
    public void Add(string date, string sizeKey)
    {
        var sizes = _dateSizes.GetOrAdd(date, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
        sizes.TryAdd(sizeKey, 0);
    }

    /// <summary>
    /// Returns all dates with their available sizes, filtered to on-or-before the given date,
    /// ordered descending by date.
    /// </summary>
    public IReadOnlyList<PuzzleDateEntry> GetDates(DateOnly upToDate)
    {
        EnsureInitialScan();

        var cutoff = upToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return _dateSizes
            .Where(kv => string.CompareOrdinal(kv.Key, cutoff) <= 0)
            .OrderByDescending(kv => kv.Key)
            .Select(kv => new PuzzleDateEntry(kv.Key, kv.Value.Keys.Order().ToArray()))
            .ToList();
    }

    private void EnsureInitialScan()
    {
        if (_initialScanDone) return;
        ScanExistingFiles();
        _initialScanDone = true;
    }

    private void ScanExistingFiles()
    {
        if (!Directory.Exists(_puzzlePath)) return;

        foreach (var f in Directory.EnumerateFiles(_puzzlePath, "puzzle-*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(f).Replace("puzzle-", "");
            string datePart;
            string sizeKey;
            if (name.Length > 10 && name[10] == '-')
            {
                datePart = name[..10];
                var sizePart = name[11..];
                sizeKey = string.Equals(sizePart, "small", StringComparison.OrdinalIgnoreCase) ? "10x10" : sizePart;
            }
            else
            {
                datePart = name;
                sizeKey = "17x17";
            }

            if (DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out _))
                Add(datePart, sizeKey);
        }
    }
}

record PuzzleDateEntry(string Date, string[] Sizes);
