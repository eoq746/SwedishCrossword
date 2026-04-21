namespace SwedishCrossword.Api;

internal static class TimeProviderExtensions
{
    private static readonly TimeZoneInfo SwedishTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    /// <summary>
    /// Returns the current date in the Europe/Stockholm time zone.
    /// </summary>
    internal static DateOnly GetSwedishDate(this TimeProvider timeProvider)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var swedishTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, SwedishTimeZone);
        return DateOnly.FromDateTime(swedishTime);
    }
}
