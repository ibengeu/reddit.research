using System.Text.Json.Serialization;

namespace RedditCrawler.Models;

/// <summary>
/// Arctic Shift API response wrapper: {"data": [...]}
/// </summary>
public sealed class ArcticShiftResponse<T>
{
    [JsonPropertyName("data")]
    public List<T> Data { get; set; } = [];
}

/// <summary>
/// Unified model for both posts and comments from Arctic Shift.
/// Fields not present on a given type will be null/default.
/// </summary>
public sealed class RedditPost
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("selftext")]
    public string? SelfText { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = "[deleted]";

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("created_utc")]
    public double CreatedUtc { get; set; }

    [JsonPropertyName("link_flair_text")]
    public string? Flair { get; set; }

    [JsonPropertyName("num_comments")]
    public int NumComments { get; set; }

    [JsonPropertyName("subreddit")]
    public string Subreddit { get; set; } = "";

    [JsonPropertyName("permalink")]
    public string Permalink { get; set; } = "";

    // Comment-specific fields
    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("link_id")]
    public string? LinkId { get; set; }

    // Comment tree children (from /api/comments/tree)
    [JsonPropertyName("children")]
    public List<RedditPost>? Children { get; set; }

    public DateTimeOffset Timestamp =>
        DateTimeOffset.FromUnixTimeSeconds((long)CreatedUtc);

    public string TextContent =>
        !string.IsNullOrWhiteSpace(Body) ? Body
        : !string.IsNullOrWhiteSpace(SelfText) ? SelfText
        : Title ?? "";

    public bool IsTopLevel => ParentId?.StartsWith("t3_") == true;

    /// Set by FlattenTree during comment tree traversal — exact depth without any post-hoc recomputation.
    public int ComputedDepth { get; set; }
}
