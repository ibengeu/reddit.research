using System.Text.Json.Serialization;

namespace RedditCrawler.Models;

public sealed class LlmChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "reddit";

    [JsonPropertyName("subreddit")]
    public string Subreddit { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "post";

    [JsonPropertyName("parent_id")]
    public string? ParentId { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("metadata")]
    public ChunkMetadata Metadata { get; set; } = new();

    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}

public sealed class ChunkMetadata
{
    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("flair")]
    public string? Flair { get; set; }

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("total_chunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("permalink")]
    public string? Permalink { get; set; }

    // Post title carried on every chunk (comments inherit from their parent post)
    // so embeddings and prompts always have topic context.
    [JsonPropertyName("post_title")]
    public string? PostTitle { get; set; }
}
