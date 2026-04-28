using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace SwedishCrossword.Api;

/// <summary>
/// Generates and validates HMAC-signed submission tokens for puzzle score submissions.
/// A token is issued when a puzzle is fetched and required when submitting a score,
/// proving the submitter actually loaded the puzzle from the server.
/// </summary>
sealed class SubmissionTokenService
{
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;
    private const double MinSecondsPerCell = 0.3;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(48);

    public SubmissionTokenService(IConfiguration config, ILogger<SubmissionTokenService> logger, TimeProvider timeProvider, IHostEnvironment environment)
    {
        var secret = config["SubmissionToken:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (environment.IsProduction())
                throw new InvalidOperationException(
                    "SubmissionToken:Secret must be configured in production. " +
                    "Set the SubmissionToken__Secret environment variable.");

            // Ephemeral key — tokens won't survive app restart.
            logger.LogWarning(
                "SubmissionToken:Secret is not configured. Using an ephemeral key — " +
                "tokens will not survive app restarts. Set the SubmissionToken__Secret " +
                "environment variable in production.");
            _key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        }
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Parses puzzle JSON, injects a <c>submissionToken</c>, <c>cellCount</c> and
    /// <c>puzzleDate</c> field, and returns the modified JSON. If the JSON has no
    /// <c>cells</c> array the original string is returned unchanged.
    /// </summary>
    public string InjectToken(string puzzleJson, DateOnly puzzleDate)
    {
        try
        {
            var node = JsonNode.Parse(puzzleJson);
            if (node is not JsonObject obj || !obj.ContainsKey("cells"))
                return puzzleJson;

            var (puzzleHash, cellCount) = ComputePuzzleMetadata(obj);
            var dateString = puzzleDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            obj["submissionToken"] = GenerateToken(puzzleHash, cellCount, dateString);
            obj["puzzleHash"] = puzzleHash;
            obj["cellCount"] = cellCount;
            obj["puzzleDate"] = dateString;

            StripAnswers(obj);

            return obj.ToJsonString();
        }
        catch
        {
            return puzzleJson;
        }
    }

    /// <summary>
    /// Generates an HMAC-signed token embedding the puzzle hash, cell count, and
    /// the puzzle date. Including the date in the signed payload prevents replay
    /// of a (hash, time) pair against a different daily leaderboard.
    /// </summary>
    public string GenerateToken(string puzzleHash, int cellCount, string puzzleDate)
    {
        var issuedAt = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var payload = $"{puzzleHash}:{cellCount}:{issuedAt}:{puzzleDate}";
        var hmac = ComputeHmac(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}:{hmac}"));
    }

    /// <summary>
    /// Validates a submission token against the expected puzzle hash, expected
    /// puzzle date, and solve time.
    /// </summary>
    public TokenValidationResult Validate(string token, string expectedPuzzleHash, string expectedPuzzleDate, double solveTimeSeconds)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length != 5)
                return TokenValidationResult.Fail("Invalid token format");

            var puzzleHash = parts[0];
            if (!int.TryParse(parts[1], out var cellCount))
                return TokenValidationResult.Fail("Invalid cell count in token");
            if (!long.TryParse(parts[2], out var issuedAt))
                return TokenValidationResult.Fail("Invalid timestamp in token");
            var puzzleDate = parts[3];
            var providedHmac = parts[4];

            // Verify HMAC
            var payload = $"{puzzleHash}:{cellCount}:{issuedAt}:{puzzleDate}";
            var expectedHmac = ComputeHmac(payload);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedHmac),
                Encoding.UTF8.GetBytes(expectedHmac)))
                return TokenValidationResult.Fail("Token signature invalid");

            // Verify puzzle hash matches
            if (puzzleHash != expectedPuzzleHash)
                return TokenValidationResult.Fail("Puzzle hash mismatch");

            // Verify puzzle date matches (prevents replaying a token across
            // different daily leaderboards).
            if (!string.IsNullOrEmpty(expectedPuzzleDate) && puzzleDate != expectedPuzzleDate)
                return TokenValidationResult.Fail("Puzzle date mismatch");

            // Verify time window
            var issuedAtTime = DateTimeOffset.FromUnixTimeSeconds(issuedAt);
            var age = _timeProvider.GetUtcNow() - issuedAtTime;
            if (age < TimeSpan.Zero || age > TokenLifetime)
                return TokenValidationResult.Fail("Token expired");

            // Verify minimum solve time
            var minTime = cellCount * MinSecondsPerCell;
            if (solveTimeSeconds < minTime)
                return TokenValidationResult.Fail($"Solve time too fast: {solveTimeSeconds:F1}s < {minTime:F1}s minimum");

            return TokenValidationResult.Ok(puzzleHash, cellCount);
        }
        catch
        {
            return TokenValidationResult.Fail("Token parsing failed");
        }
    }

    /// <summary>
    /// Validates a submission token for puzzle access (HMAC + expiry).
    /// Optionally verifies expected puzzle hash/date to prevent cross-puzzle replay.
    /// </summary>
    public TokenValidationResult ValidateAccess(string token, string? expectedPuzzleHash = null, string? expectedPuzzleDate = null)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length != 5)
                return TokenValidationResult.Fail("Invalid token format");

            var puzzleHash = parts[0];
            if (!int.TryParse(parts[1], out var cellCount))
                return TokenValidationResult.Fail("Invalid cell count in token");
            if (!long.TryParse(parts[2], out var issuedAt))
                return TokenValidationResult.Fail("Invalid timestamp in token");
            var puzzleDate = parts[3];
            var providedHmac = parts[4];

            var payload = $"{puzzleHash}:{cellCount}:{issuedAt}:{puzzleDate}";
            var expectedHmac = ComputeHmac(payload);
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedHmac),
                Encoding.UTF8.GetBytes(expectedHmac)))
                return TokenValidationResult.Fail("Token signature invalid");

            if (!string.IsNullOrEmpty(expectedPuzzleHash) && puzzleHash != expectedPuzzleHash)
                return TokenValidationResult.Fail("Puzzle hash mismatch");

            if (!string.IsNullOrEmpty(expectedPuzzleDate) && puzzleDate != expectedPuzzleDate)
                return TokenValidationResult.Fail("Puzzle date mismatch");

            var age = _timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(issuedAt);
            if (age < TimeSpan.Zero || age > TokenLifetime)
                return TokenValidationResult.Fail("Token expired");

            return TokenValidationResult.Ok(puzzleHash, cellCount);
        }
        catch
        {
            return TokenValidationResult.Fail("Token parsing failed");
        }
    }

    private string ComputeHmac(string payload)
    {
        var hmac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hmac);
    }

    /// <summary>
    /// Removes answer data from the puzzle JSON so it is not sent to the client.
    /// Strips <c>letter</c> from every cell and <c>answer</c> from every clue.
    /// </summary>
    internal static void StripAnswers(JsonObject obj)
    {
        // Strip letters from cells
        if (obj["cells"] is JsonArray cellRows)
        {
            foreach (var row in cellRows)
            {
                if (row is not JsonArray rowArray) continue;
                foreach (var cell in rowArray)
                {
                    if (cell is JsonObject cellObj)
                        cellObj.Remove("letter");
                }
            }
        }

        // Strip answers from clues
        if (obj["clues"] is JsonObject cluesObj)
        {
            foreach (var dir in new[] { "across", "down" })
            {
                if (cluesObj[dir] is not JsonArray clueArray) continue;
                foreach (var clue in clueArray)
                {
                    if (clue is JsonObject clueObj)
                        clueObj.Remove("answer");
                }
            }
        }
    }

    /// <summary>
    /// Reads the answer map ("row,col" → letter) from a puzzle JSON file on disk.
    /// Returns null if the file does not exist or cannot be parsed.
    /// </summary>
    internal static async Task<Dictionary<string, string>?> ReadAnswersAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj || obj["cells"] is not JsonArray cellRows)
                return null;

            var answers = new Dictionary<string, string>();
            for (int row = 0; row < cellRows.Count; row++)
            {
                if (cellRows[row] is not JsonArray rowArray) continue;
                for (int col = 0; col < rowArray.Count; col++)
                {
                    if (rowArray[col] is JsonObject cellObj &&
                        cellObj["letter"]?.GetValue<string>() is { Length: > 0 } letter)
                    {
                        answers[$"{row},{col}"] = letter;
                    }
                }
            }
            return answers;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Computes puzzle hash and cell count from the parsed puzzle JSON.
    /// The hash algorithm mirrors the client-side <c>generatePuzzleHash()</c>.
    /// </summary>
    internal static (string Hash, int CellCount) ComputePuzzleMetadata(JsonObject obj)
    {
        var cells = obj["cells"]!.AsArray();
        var width = obj["width"]?.GetValue<int>() ?? 0;

        int cellCount = 0;
        var sb = new StringBuilder();

        foreach (var row in cells)
        {
            if (row is not JsonArray rowArray) continue;
            var rowWidth = width > 0 ? width : rowArray.Count;
            for (int col = 0; col < rowWidth; col++)
            {
                var cell = col < rowArray.Count ? rowArray[col] : null;
                if (cell != null)
                {
                    cellCount++;
                    sb.Append(cell["letter"]?.GetValue<string>() ?? "#");
                }
                else
                {
                    sb.Append('#');
                }
            }
        }

        return (JavaStringHash(sb.ToString()), cellCount);
    }

    /// <summary>
    /// Java-style <c>String.hashCode()</c> converted to base-36,
    /// matching the client-side <c>generatePuzzleHash()</c>.
    /// </summary>
    private static string JavaStringHash(string s)
    {
        unchecked
        {
            int hash = 0;
            foreach (char c in s)
                hash = ((hash << 5) - hash) + c;
            return ToBase36(hash);
        }
    }

    private static string ToBase36(int value)
    {
        if (value == 0) return "0";
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        bool negative = value < 0;
        long v = negative ? -(long)value : value;
        var result = new char[14];
        int pos = result.Length;
        while (v > 0)
        {
            result[--pos] = digits[(int)(v % 36)];
            v /= 36;
        }
        if (negative) result[--pos] = '-';
        return new string(result, pos, result.Length - pos);
    }
}

sealed record TokenValidationResult(bool IsValid, string? Error, string? PuzzleHash, int CellCount)
{
    public static TokenValidationResult Ok(string puzzleHash, int cellCount) => new(true, null, puzzleHash, cellCount);
    public static TokenValidationResult Fail(string error) => new(false, error, null, 0);
}
