using System.Net.Http.Headers;
using System.Text.Json;

// ---------------------------------------------------------------------------
// Cloudflare KV → LeaderboardStore Migration Tool
// ---------------------------------------------------------------------------
//
// Exports leaderboard history from a Cloudflare Workers KV namespace and
// writes the data as JSON files compatible with LeaderboardStore.
//
// Required environment variables:
//   CF_ACCOUNT_ID   — Cloudflare account ID
//   CF_NAMESPACE_ID — KV namespace ID (from wrangler.toml / Cloudflare dashboard)
//   CF_API_TOKEN    — API token with Workers KV read permission
//
// Usage:
//   dotnet run [output-directory]
//
// Default output: ./leaderboard-export
//
// After export, upload to Azure Files:
//   az storage file upload-batch \
//     --source ./leaderboard-export/history \
//     --destination crossword-data \
//     --destination-path leaderboard/history \
//     --account-name <storage-account-name>
// ---------------------------------------------------------------------------

var accountId = GetRequiredEnvVar("CF_ACCOUNT_ID");
var namespaceId = GetRequiredEnvVar("CF_NAMESPACE_ID");
var apiToken = GetRequiredEnvVar("CF_API_TOKEN");

var outputDir = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "leaderboard-export");

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Cloudflare KV → LeaderboardStore Migration Tool");
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine($"  Account ID:   {accountId[..Math.Min(8, accountId.Length)]}...");
Console.WriteLine($"  Namespace ID: {namespaceId[..Math.Min(8, namespaceId.Length)]}...");
Console.WriteLine($"  Output:       {Path.GetFullPath(outputDir)}");
Console.WriteLine();

var historyDir = Path.Combine(outputDir, "history");
Directory.CreateDirectory(historyDir);

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
var baseUrl = $"https://api.cloudflare.com/client/v4/accounts/{accountId}/storage/kv/namespaces/{namespaceId}";

// ---------------------------------------------------------------------------
// 1. List all leaderboard-history keys
// ---------------------------------------------------------------------------
Console.WriteLine("Listing history keys...");
var keys = await ListKeysAsync(http, baseUrl, "leaderboard-history:");
Console.WriteLine($"Found {keys.Count} history entries");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 2. Fetch and write each history entry
// ---------------------------------------------------------------------------
int migrated = 0, skipped = 0, failed = 0;

foreach (var key in keys)
{
    var date = key.Replace("leaderboard-history:", "");
    var targetPath = Path.Combine(historyDir, $"{date}.json");

    if (File.Exists(targetPath))
    {
        Console.WriteLine($"  SKIP  {date} (file already exists)");
        skipped++;
        continue;
    }

    try
    {
        var response = await http.GetAsync($"{baseUrl}/values/{Uri.EscapeDataString(key)}");
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  FAIL  {date} (HTTP {(int)response.StatusCode})");
            failed++;
            continue;
        }

        var rawJson = await response.Content.ReadAsStringAsync();
        var normalized = NormalizeHistoryRecords(rawJson);

        await File.WriteAllTextAsync(targetPath, normalized);
        Console.WriteLine($"  OK    {date}");
        migrated++;

        // Small delay to avoid Cloudflare rate limits
        await Task.Delay(50);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR {date}: {ex.Message}");
        failed++;
    }
}

// ---------------------------------------------------------------------------
// 3. Fetch current leaderboard (optional — only useful if the Worker is still live)
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("Fetching current leaderboard...");
try
{
    var response = await http.GetAsync($"{baseUrl}/values/{Uri.EscapeDataString("leaderboard:current")}");
    if (response.IsSuccessStatusCode)
    {
        var json = await response.Content.ReadAsStringAsync();
        var currentPath = Path.Combine(outputDir, "current.json");
        await File.WriteAllTextAsync(currentPath, json);
        Console.WriteLine("  OK    current.json");
    }
    else
    {
        Console.WriteLine($"  SKIP  current (HTTP {(int)response.StatusCode})");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  ERROR current: {ex.Message}");
}

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine($"  Results: {migrated} migrated, {skipped} skipped, {failed} failed");
Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("Next steps:");
Console.WriteLine($"  1. Review exported files in: {Path.GetFullPath(outputDir)}");
Console.WriteLine("  2. Find your storage account name:");
Console.WriteLine("       az storage account list -g rg-svensktkorsord --query \"[].name\" -o tsv");
Console.WriteLine("  3. Upload history to Azure Files:");
Console.WriteLine("       az storage file upload-batch \\");
Console.WriteLine($"         --source \"{Path.GetFullPath(historyDir)}\" \\");
Console.WriteLine("         --destination crossword-data \\");
Console.WriteLine("         --destination-path leaderboard/history \\");
Console.WriteLine("         --account-name <storage-account-name>");

return;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

static string GetRequiredEnvVar(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException(
        $"Missing required environment variable: {name}. " +
        $"Set it with: $env:{name} = 'your-value'");

static async Task<List<string>> ListKeysAsync(HttpClient http, string baseUrl, string prefix)
{
    var keys = new List<string>();
    string? cursor = null;

    do
    {
        var url = $"{baseUrl}/keys?prefix={Uri.EscapeDataString(prefix)}&limit=1000";
        if (cursor is not null)
            url += $"&cursor={Uri.EscapeDataString(cursor)}";

        var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        foreach (var key in doc.RootElement.GetProperty("result").EnumerateArray())
            keys.Add(key.GetProperty("name").GetString()!);

        // Cloudflare returns a cursor for pagination; empty/missing means last page
        cursor = doc.RootElement.TryGetProperty("result_info", out var info)
              && info.TryGetProperty("cursor", out var c)
              && c.ValueKind == JsonValueKind.String
              && c.GetString() is { Length: > 0 } cv
            ? cv
            : null;
    } while (cursor is not null);

    return keys;
}

/// <summary>
/// Normalizes Cloudflare KV history records to the format expected by LeaderboardStore.
/// Key transformation: timestamp is a number in KV but must be a string in the file store.
/// </summary>
static string NormalizeHistoryRecords(string json)
{
    using var doc = JsonDocument.Parse(json);
    var records = new List<NormalizedRecord>();

    foreach (var el in doc.RootElement.EnumerateArray())
    {
        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var time = el.TryGetProperty("time", out var t) ? t.GetDouble() : 0.0;

        string? timestamp = null;
        if (el.TryGetProperty("timestamp", out var ts))
        {
            timestamp = ts.ValueKind switch
            {
                JsonValueKind.Number => ts.GetInt64().ToString(),
                JsonValueKind.String => ts.GetString(),
                _ => null
            };
        }

        var puzzleHash = el.TryGetProperty("puzzleHash", out var ph) ? ph.GetString() : null;

        records.Add(new NormalizedRecord(name, time, timestamp, puzzleHash));
    }

    return JsonSerializer.Serialize(records, NormalizedRecord.SerializerOptions);
}

/// <summary>
/// Matches the HistoryRecord shape in LeaderboardStore exactly.
/// </summary>
record NormalizedRecord(string Name, double Time, string? Timestamp, string? PuzzleHash)
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
