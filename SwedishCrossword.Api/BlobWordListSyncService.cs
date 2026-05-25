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
        var result = WordListMerger.MergeThreeWay(baseJson, devJson, prodJson, fileName);
        return new MergeResult(
            result.MergedJson, result.Added, result.Updated, result.Removed, result.Conflicts,
            result.ConflictDetails.Select(c => new BlobWordListSyncConflictDetail(c.Word, c.Reason, c.Resolution)).ToList());
    }

    private static bool JsonEquals(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private sealed record BlobContent(string Content, ETag ETag);
    private sealed record MergeResult(string MergedJson, int Added, int Updated, int Removed, int Conflicts, List<BlobWordListSyncConflictDetail> ConflictDetails);
}
