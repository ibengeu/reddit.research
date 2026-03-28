using RedditCrawler.Models;
using RedditCrawler.Services;

namespace RedditCrawler.Tests;

public class VectorStoreStatsTests
{
    [Fact]
    public async Task NoOp_GetStats_ReturnsDisabledStats()
    {
        var svc = new NoOpVectorStoreService();
        var stats = await svc.GetStatsAsync();
        Assert.False(stats.IsEnabled);
        Assert.Equal(0ul, stats.TotalChunks);
        Assert.Empty(stats.Subreddits);
    }
}
