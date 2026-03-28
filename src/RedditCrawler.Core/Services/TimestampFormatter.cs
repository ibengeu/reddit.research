namespace RedditCrawler.Services;

public static class TimestampFormatter
{
    /// <summary>
    /// Parses an ISO 8601 or Unix-seconds timestamp string and formats it as "yyyy-MM-dd".
    /// Returns null if the input is null or cannot be parsed.
    /// </summary>
    public static string? FormatDate(string? timestamp)
    {
        if (timestamp is null) return null;
        if (DateTimeOffset.TryParse(timestamp, out var iso))
            return iso.ToString("yyyy-MM-dd");
        if (long.TryParse(timestamp, out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix).ToString("yyyy-MM-dd");
        return null;
    }
}
