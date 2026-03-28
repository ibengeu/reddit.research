using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RedditCrawler.Configuration;
using RedditCrawler.Models;

namespace RedditCrawler.Services;

public interface IRedditClient
{
    Task<List<RedditPost>> GetPostsAsync(string subreddit, int limit, string sort = "desc", string? before = null, CancellationToken ct = default);
    Task<List<RedditPost>> GetCommentsAsync(string postId, CancellationToken ct = default);
}

public sealed class ArcticShiftClient : IRedditClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ArcticShiftClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 512
    };

    public ArcticShiftClient(HttpClient http, ILogger<ArcticShiftClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<RedditPost>> GetPostsAsync(
        string subreddit, int limit, string sort = "desc", string? before = null, CancellationToken ct = default)
    {
        var url = $"/api/posts/search?subreddit={Uri.EscapeDataString(subreddit)}&limit={limit}&sort={Uri.EscapeDataString(sort)}";
        if (before is not null) url += $"&before={Uri.EscapeDataString(before)}";

        var response = await SendWithRetryAsync<ArcticShiftResponse<RedditPost>>(url, ct);
        return response.Data;
    }

    public async Task<List<RedditPost>> GetCommentsAsync(string postId, CancellationToken ct = default)
    {
        // Use comment tree endpoint for full threaded comments
        var cleanId = postId.Replace("t3_", "");
        var url = $"/api/comments/tree?link_id={cleanId}&limit=9999";

        var response = await SendWithRetryAsync<ArcticShiftResponse<RedditPost>>(url, ct);
        return FlattenTree(response.Data);
    }

    private async Task<T> SendWithRetryAsync<T>(string url, CancellationToken ct, int maxAttempts = 4)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var response = await _http.GetAsync(url, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Rate limited. Retrying after {Delay}s", delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            if ((int)response.StatusCode >= 500)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Server error {Status}. Retrying after {Delay}s",
                    (int)response.StatusCode, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
        }

        throw new HttpRequestException($"Failed after {maxAttempts} attempts: {url}");
    }

    /// <summary>
    /// Flatten the nested comment tree into a flat list, computing depth along the way.
    /// </summary>
    private static List<RedditPost> FlattenTree(List<RedditPost> roots)
    {
        var result = new List<RedditPost>();
        var seen = new HashSet<string>();
        var stack = new Stack<(RedditPost Comment, int Depth)>();

        for (var i = roots.Count - 1; i >= 0; i--)
            stack.Push((roots[i], 0));

        while (stack.Count > 0)
        {
            var (comment, depth) = stack.Pop();

            if (!seen.Add(comment.Id)) continue;  // skip duplicates

            comment.ComputedDepth = depth;
            result.Add(comment);

            if (comment.Children is { Count: > 0 } children)
                for (var i = children.Count - 1; i >= 0; i--)
                    stack.Push((children[i], depth + 1));
        }

        return result;
    }
}
