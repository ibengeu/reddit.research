using Microsoft.Extensions.Logging;
using RedditCrawler.Configuration;
using RedditCrawler.Models;

namespace RedditCrawler.Services;

public interface ICrawlerService
{
    IAsyncEnumerable<(RedditPost Post, List<RedditPost> Comments)> CrawlAsync(string subreddit, int limit, CancellationToken ct = default);
}

public sealed class CrawlerService : ICrawlerService
{
    private readonly IRedditClient _client;
    private readonly CrawlerConfig _config;
    private readonly ILogger<CrawlerService> _logger;

    public CrawlerService(IRedditClient client, CrawlerConfig config, ILogger<CrawlerService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async IAsyncEnumerable<(RedditPost Post, List<RedditPost> Comments)> CrawlAsync(
        string subreddit,
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var fetched = 0;
        string? before = null;

        while (fetched < limit)
        {
            var batchSize = Math.Min(100, limit - fetched);

            _logger.LogInformation("Fetching posts {Start}-{End} from r/{Sub}...",
                fetched + 1, fetched + batchSize, subreddit);

            var posts = await _client.GetPostsAsync(subreddit, batchSize, _config.Sort, before, ct);

            if (posts.Count == 0)
            {
                _logger.LogInformation("No more posts available in r/{Sub}", subreddit);
                break;
            }

            foreach (var post in posts)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogDebug("Fetching comments for post {Id}: {Title}", post.Id, post.Title);
                var comments = await _client.GetCommentsAsync(post.Id, ct);

                _logger.LogDebug("Got {Count} comments for post {Id}", comments.Count, post.Id);

                yield return (post, comments);
                fetched++;

                if (fetched >= limit) break;
            }

            var lastPost = posts[^1];
            before = ((long)lastPost.CreatedUtc).ToString();
        }

        _logger.LogInformation("Crawl of r/{Sub} complete. Processed {Count} posts", subreddit, fetched);
    }
}
