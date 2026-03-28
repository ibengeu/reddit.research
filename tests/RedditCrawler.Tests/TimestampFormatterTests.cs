using RedditCrawler.Services;

namespace RedditCrawler.Tests;

public class TimestampFormatterTests
{
    [Theory]
    [InlineData("2024-03-15T10:30:00+00:00", "2024-03-15")]
    [InlineData("2024-03-15T00:00:00Z", "2024-03-15")]
    [InlineData("2024-12-31T23:59:59+00:00", "2024-12-31")]
    public void FormatDate_Iso8601_ReturnsExpectedDate(string input, string expected)
    {
        Assert.Equal(expected, TimestampFormatter.FormatDate(input));
    }

    [Fact]
    public void FormatDate_UnixSeconds_ReturnsExpectedDate()
    {
        // 2024-03-15 00:00:00 UTC = 1710460800
        var result = TimestampFormatter.FormatDate("1710460800");
        Assert.Equal("2024-03-15", result);
    }

    [Fact]
    public void FormatDate_Null_ReturnsNull()
    {
        Assert.Null(TimestampFormatter.FormatDate(null));
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("")]
    [InlineData("abc123")]
    public void FormatDate_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(TimestampFormatter.FormatDate(input));
    }
}
