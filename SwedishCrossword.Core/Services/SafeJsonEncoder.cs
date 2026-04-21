using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace SwedishCrossword.Services;

/// <summary>
/// Provides a shared <see cref="JavaScriptEncoder"/> that allows all Unicode
/// characters (including Swedish å, ä, ö and symbols like &amp; and +) to pass
/// through unescaped, while still escaping HTML-sensitive characters (&lt; and
/// &gt;) as a defense-in-depth measure.
/// </summary>
public static class SafeJsonEncoder
{
    /// <summary>
    /// A <see cref="JavaScriptEncoder"/> safe for JSON files that are loaded via
    /// <c>JsonSerializer.Deserialize</c> or <c>fetch()</c> — never embedded raw
    /// in HTML.  Escapes only &lt; and &gt; to prevent accidental HTML injection.
    /// </summary>
    public static JavaScriptEncoder Instance { get; } =
        JavaScriptEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Pre-configured <see cref="JsonSerializerOptions"/> with indented output
    /// and the safe encoder.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = Instance
    };

    /// <summary>
    /// Pre-configured <see cref="JsonSerializerOptions"/> for deserialization
    /// with case-insensitive property matching and the safe encoder.
    /// </summary>
    public static JsonSerializerOptions DeserializeOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = Instance
    };
}
