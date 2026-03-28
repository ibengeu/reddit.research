namespace RedditCrawler.Models;

public sealed record ScoredChunk(
    string Content,
    string Subreddit,
    float SimilarityScore,
    int RedditScore,
    string? Flair,
    string Timestamp);
