using System.Globalization;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace SwedishCrossword.Api;

/// <summary>
/// Pre-processed puzzle data cached in memory to avoid repeated JSON DOM manipulation.
/// </summary>
sealed record PreparedPuzzle(string StrippedJsonTemplate, string PuzzleHash, int CellCount);

/// <summary>
/// In-memory cache for puzzle file contents, pre-processed templates, and parsed answer maps.
/// Avoids repeated disk I/O and JSON parsing for the same puzzle files.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
sealed class PuzzleCache
{
    private readonly ConcurrentDictionary<string, string> _jsonCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Dictionary<string, string>?> _answersCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PreparedPuzzle?> _preparedCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the raw JSON content of a puzzle file, reading from disk only on first access.
    /// </summary>
    public async Task<string?> GetJsonAsync(string filePath, CancellationToken ct = default)
    {
        if (_jsonCache.TryGetValue(filePath, out var cached))
            return cached;

        if (!File.Exists(filePath))
            return null;

        var json = await File.ReadAllTextAsync(filePath, ct);
        _jsonCache.TryAdd(filePath, json);
        return json;
    }

    /// <summary>
    /// Gets the pre-processed puzzle (stripped JSON template with a placeholder token, hash, and cell count).
    /// The template contains a placeholder <c>__TOKEN__</c> that callers replace with the actual token.
    /// Parses the JSON DOM only on first access per file.
    /// </summary>
    public async Task<PreparedPuzzle?> GetPreparedAsync(string filePath, DateOnly puzzleDate, CancellationToken ct = default)
    {
        if (_preparedCache.TryGetValue(filePath, out var cached))
            return cached;

        var json = await GetJsonAsync(filePath, ct);
        if (json is null)
            return null;

        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj || !obj.ContainsKey("cells"))
            {
                _preparedCache.TryAdd(filePath, null);
                return null;
            }

            var (puzzleHash, cellCount) = SubmissionTokenService.ComputePuzzleMetadata(obj);

            // Insert a placeholder token that will be swapped cheaply per-request
            obj["submissionToken"] = "__TOKEN__";
            obj["puzzleHash"] = puzzleHash;
            obj["cellCount"] = cellCount;
            obj["puzzleDate"] = puzzleDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            SubmissionTokenService.StripAnswers(obj);

            var template = obj.ToJsonString();
            var prepared = new PreparedPuzzle(template, puzzleHash, cellCount);
            _preparedCache.TryAdd(filePath, prepared);
            return prepared;
        }
        catch
        {
            _preparedCache.TryAdd(filePath, null);
            return null;
        }
    }

    /// <summary>
    /// Gets the parsed answer map for a puzzle file, parsing only on first access.
    /// </summary>
    public async Task<Dictionary<string, string>?> GetAnswersAsync(string filePath)
    {
        if (_answersCache.TryGetValue(filePath, out var cached))
            return cached;

        var answers = await SubmissionTokenService.ReadAnswersAsync(filePath);
        _answersCache.TryAdd(filePath, answers);
        return answers;
    }

    /// <summary>
    /// Invalidates cached entries for a specific file (e.g., after regeneration).
    /// </summary>
    public void Invalidate(string filePath)
    {
        _jsonCache.TryRemove(filePath, out _);
        _answersCache.TryRemove(filePath, out _);
        _preparedCache.TryRemove(filePath, out _);
    }
}
