using RedditCrawler.Configuration;
using RedditCrawler.Models;

namespace RedditCrawler.Services;

public interface ITransformer
{
    IEnumerable<LlmChunk> Transform(RedditPost post, List<RedditPost> comments);
}

public sealed class Transformer : ITransformer
{
    private readonly int _maxTokens;
    private readonly int _overlapTokens;

    public Transformer(CrawlerConfig config)
    {
        _maxTokens = config.MaxTokensPerChunk;
        _overlapTokens = config.ChunkOverlapTokens;
    }

    public IEnumerable<LlmChunk> Transform(RedditPost post, List<RedditPost> comments)
    {
        var postTitle = string.IsNullOrWhiteSpace(post.Title) ? null : TextCleaner.Clean(post.Title);

        foreach (var chunk in ToPostChunks(post, postTitle))
            yield return chunk;

        foreach (var comment in comments)
        {
            var parentId = comment.ParentId?.StartsWith("t3_") == true
                ? $"post_{post.Id}"
                : $"comment_{comment.ParentId?.Replace("t1_", "")}";

            foreach (var chunk in ToCommentChunks(comment, parentId, comment.ComputedDepth, postTitle))
                yield return chunk;
        }
    }

    // Posts: prepend title so every chunk carries topic context.
    private IEnumerable<LlmChunk> ToPostChunks(RedditPost post, string? postTitle)
    {
        var body = TextCleaner.Clean(post.TextContent);
        if (string.IsNullOrWhiteSpace(body)) yield break;

        var content = string.IsNullOrWhiteSpace(postTitle)
            ? body
            : $"[Post] {postTitle}\n\n{body}";

        foreach (var chunk in SplitToChunks(content, "post", post, parentId: null, depth: 0, postTitle))
            yield return chunk;
    }

    // Comments: structural-first — keep the whole comment as one chunk when it fits.
    // Only fall back to overlapping splitter for oversized wall-of-text comments.
    private IEnumerable<LlmChunk> ToCommentChunks(
        RedditPost comment, string? parentId, int depth, string? postTitle)
    {
        var body = TextCleaner.Clean(comment.TextContent);
        if (string.IsNullOrWhiteSpace(body)) yield break;

        foreach (var chunk in SplitToChunks(body, "comment", comment, parentId, depth, postTitle))
            yield return chunk;
    }

    private IEnumerable<LlmChunk> SplitToChunks(
        string content, string type, RedditPost item,
        string? parentId, int depth, string? postTitle)
    {
        var textChunks = TextCleaner.ChunkText(content, _maxTokens, _overlapTokens);

        for (var i = 0; i < textChunks.Count; i++)
        {
            var chunkId = textChunks.Count == 1
                ? $"{type}_{item.Id}"
                : $"{type}_{item.Id}_chunk_{i}";

            yield return new LlmChunk
            {
                Id = chunkId,
                Subreddit = item.Subreddit,
                Type = type,
                ParentId = parentId,
                Author = item.Author,
                Timestamp = item.Timestamp.ToString("o"),
                Content = textChunks[i],
                Metadata = new ChunkMetadata
                {
                    Score = item.Score,
                    Flair = item.Flair,
                    Depth = depth,
                    ChunkIndex = i,
                    TotalChunks = textChunks.Count,
                    Permalink = item.Permalink,
                    PostTitle = postTitle
                }
            };
        }
    }

}
