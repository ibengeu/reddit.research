namespace RedditCrawler.Models;

public sealed record SubredditStat(string Name, ulong ChunkCount);

public sealed record VectorStoreStats(
    ulong TotalChunks,
    IReadOnlyList<SubredditStat> Subreddits,
    bool IsEnabled);
