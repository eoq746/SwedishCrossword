using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text;
using System.Text.Json;
using SwedishCrossword.Services;

namespace SwedishCrossword.Api;

internal sealed class BlobWordListSyncService
{
    private const string DefaultBaselinePrefix = "sync-base";

    private static readonly JsonSerializerOptions JsonOptions = SafeJsonEncoder.DefaultOptions;

    private static readonly string[] WordListFiles =
    [
        "lexin-words.json",
        "synonym-words.json",
        "kelly-words.json",
        "dsso-words.json",
        "custom-words.json"
    ];

    private readonly ILogger<BlobWordListSyncService> _logger;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly string? _devContainer;
    private readonly string? _prodContainer;
    private readonly string? _baselineContainer;
    private readonly string? _baselinePrefix;

    public BlobWordListSyncService(IConfiguration config, ILogger<BlobWordListSyncService> logger)
    {
        _logger = logger;

        var section = config.GetSection("WordListSync");
        if (!section.GetValue<bool>("Enabled"))
            return;

        _devContainer = section["DevContainer"] ?? "wordlists-dev";
        _prodContainer = section["ProdContainer"] ?? "wordlists-prod";
        _baselineContainer = section["BaselineContainer"] ?? _prodContainer;
        _baselinePrefix = section["BaselinePrefix"] ?? DefaultBaselinePrefix;

        var connectionString = section["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _blobServiceClient = new BlobServiceClient(connectionString);
            return;
        }

        var serviceUriText = section["ServiceUri"];
        if (string.IsNullOrWhiteSpace(serviceUriText))
            throw new InvalidOperationException("WordListSync:ServiceUri is required when connection string is not configured.");

        if (!Uri.TryCreate(serviceUriText, UriKind.Absolute, out var serviceUri))
            throw new InvalidOperationException("WordListSync:ServiceUri is invalid.");

        TokenCredential credential = new DefaultAzureCredential();
        _blobServiceClient = new BlobServiceClient(serviceUri, credential);
    }

    public bool IsEnabled => _blobServiceClient is not null;

    public async Task<BlobWordListSyncResponse> SyncDevToProdAsync(bool dryRun, CancellationToken ct)
    {
        if (_blobServiceClient is null)
            throw new InvalidOperationException("Blob word list sync is disabled in configuration.");

        var dev = _blobServiceClient.GetBlobContainerClient(_devContainer!);
        var prod = _blobServiceClient.GetBlobContainerClient(_prodContainer!);
        var baseline = _blobServiceClient.GetBlobContainerClient(_baselineContainer!);

        if (!dryRun)
            await baseline.CreateIfNotExistsAsync(cancellationToken: ct);

        var fileResults = new List<BlobWordListSyncFileResult>(WordListFiles.Length);

        foreach (var file in WordListFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var devBlob = dev.GetBlobClient(file);
                var prodBlob = prod.GetBlobClient(file);
                var baseBlob = baseline.GetBlobClient($"{_baselinePrefix!}/{file}");

                var devContent = await TryDownloadAsync(devBlob, ct);
                var prodContent = await TryDownloadAsync(prodBlob, ct);
                var baseContent = await TryDownloadAsync(baseBlob, ct);

                if (devContent is null && prodContent is null)
                {
                    fileResults.Add(new BlobWordListSyncFileResult(file, 0, 0, 0, 0, false));
                    continue;
                }

                var baseJson = baseContent?.Content ?? prodContent?.Content;
                var merge = MergeThreeWay(baseJson, devContent?.Content, prodContent?.Content, file);

                var changed = !JsonEquals(merge.MergedJson, prodContent?.Content);

                if (!dryRun && changed)
                {
                    await UploadMergedAsync(prodBlob, merge.MergedJson, prodContent?.ETag, ct);
                    await UploadMergedAsync(baseBlob, merge.MergedJson, baseContent?.ETag, ct);
                }

                fileResults.Add(new BlobWordListSyncFileResult(
                    file,
                    merge.Added,
                    merge.Updated,
                    merge.Removed,
                    merge.Conflicts,
                    changed,
                    merge.ConflictDetails));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blob sync failed for file {File}", file);
                fileResults.Add(new BlobWordListSyncFileResult(file, 0, 0, 0, 0, false, null, ex.Message));
            }
        }

        return new BlobWordListSyncResponse(
            DryRun: dryRun,
            FilesProcessed: fileResults.Count,
            FilesChanged: fileResults.Count(f => f.Changed),
            TotalAdded: fileResults.Sum(f => f.Added),
            TotalUpdated: fileResults.Sum(f => f.Updated),
            TotalRemoved: fileResults.Sum(f => f.Removed),
            TotalConflicts: fileResults.Sum(f => f.Conflicts),
            Files: fileResults);
    }

    private static async Task<BlobContent?> TryDownloadAsync(BlobClient blob, CancellationToken ct)
    {
        try
        {
            var response = await blob.DownloadContentAsync(ct);
            return new BlobContent(response.Value.Content.ToString(), response.Value.Details.ETag);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static async Task UploadMergedAsync(BlobClient blob, string json, ETag? currentEtag, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes, writable: false);

        var options = new BlobUploadOptions
        {
            Conditions = currentEtag.HasValue
                ? new BlobRequestConditions { IfMatch = currentEtag.Value }
                : new BlobRequestConditions { IfNoneMatch = ETag.All }
        };

        await blob.UploadAsync(stream, options, ct);
    }

    private static MergeResult MergeThreeWay(string? baseJson, string? devJson, string? prodJson, string fileName)
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
        var conflictDetails = new List<BlobWordListSyncConflictDetail>();

        foreach (var key in keys)
        {
            baseMap.TryGetValue(key, out var baseEntry);
            devMap.TryGetValue(key, out var devEntry);
            prodMap.TryGetValue(key, out var prodEntry);

            var baseExists = baseEntry is not null;
            var devExists = devEntry is not null;
            var prodExists = prodEntry is not null;

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
                conflictDetails.Add(new BlobWordListSyncConflictDetail(key, "Concurrent add mismatch", "kept-prod"));
                if (prodEntry is not null)
                    merged[key] = CloneEntry(prodEntry!);
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
                conflictDetails.Add(new BlobWordListSyncConflictDetail(key, "Deleted in dev but changed in prod", "kept-prod"));
                merged[key] = CloneEntry(prodEntry!);
                continue;
            }

            if (baseExists && devExists && !prodExists)
            {
                conflicts++;
                conflictDetails.Add(new BlobWordListSyncConflictDetail(key, "Deleted in prod but changed in dev", "kept-prod-delete"));
                continue;
            }

            var mergedEntry = MergeEntry(baseEntry!, devEntry!, prodEntry!, out var entryConflict, out var entryUpdated);
            if (entryConflict)
            {
                conflicts++;
                conflictDetails.Add(new BlobWordListSyncConflictDetail(key, "Field-level mismatch", "kept-prod-field"));
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

    private static Dictionary<string, WordEntry> ParseWordMap(string? json, string fileName)
    {
        if (string.IsNullOrWhiteSpace(json))
#pragma warning disable IDE0028 // Simplify collection initialization
            return new Dictionary<string, WordEntry>(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028 // Simplify collection initialization

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

    private static bool EntryEquals(WordEntry? left, WordEntry? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return string.Equals(left.Word, right.Word, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Clue, right.Clue, StringComparison.Ordinal)
            && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            && string.Equals(left.Difficulty, right.Difficulty, StringComparison.Ordinal)
            && SequenceEquals(left.AlternativeClues, right.AlternativeClues);
    }

    private static WordEntry CloneEntry(WordEntry source)
    {
        return new WordEntry
        {
            Word = source.Word,
            Clue = source.Clue,
            Category = source.Category,
            Difficulty = source.Difficulty,
            AlternativeClues = [.. source.AlternativeClues]
        };
    }

    private static bool JsonEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private sealed record BlobContent(string Content, ETag ETag);
    private sealed record MergeResult(string MergedJson, int Added, int Updated, int Removed, int Conflicts, List<BlobWordListSyncConflictDetail> ConflictDetails);
}
