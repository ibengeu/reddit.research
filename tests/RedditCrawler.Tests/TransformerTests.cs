using RedditCrawler.Configuration;
using RedditCrawler.Models;
using RedditCrawler.Services;

namespace RedditCrawler.Tests;

public class TransformerTests
{
    private readonly Transformer _transformer = new(new CrawlerConfig { MaxTokensPerChunk = 512, ChunkOverlapTokens = 50 });

    [Fact]
    public void Transform_PostWithNoComments_ProducesPostChunks()
    {
        var post = MakePost("p1", "Test Title", "Some body text");

        var chunks = _transformer.Transform(post, []).ToList();

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal("post", c.Type));
        Assert.All(chunks, c => Assert.Equal("reddit", c.Source));
        Assert.All(chunks, c => Assert.StartsWith("post_p1", c.Id));
    }

    [Fact]
    public void Transform_Comments_PreserveParentRelationship()
    {
        var post = MakePost("p1", "Title", "Body");
        var comment = MakeComment("c1", "t3_p1", "A comment");

        var chunks = _transformer.Transform(post, [comment]).ToList();
        var commentChunk = chunks.First(c => c.Type == "comment");

        Assert.Equal("post_p1", commentChunk.ParentId);
    }

    [Fact]
    public void Transform_NestedComment_ParentIsParentComment()
    {
        var post = MakePost("p1", "Title", "Body");
        var topComment = MakeComment("c1", "t3_p1", "Top comment");
        var reply = MakeComment("c2", "t1_c1", "A reply");

        var chunks = _transformer.Transform(post, [topComment, reply]).ToList();
        var replyChunk = chunks.First(c => c.Id.Contains("c2"));

        Assert.Equal("comment_c1", replyChunk.ParentId);
        Assert.Equal(1, replyChunk.Metadata.Depth);
    }

    [Fact]
    public void Transform_TopLevelComment_HasDepthZero()
    {
        var post = MakePost("p1", "Title", "Body");
        var comment = MakeComment("c1", "t3_p1", "Top level");

        var chunks = _transformer.Transform(post, [comment]).ToList();
        var commentChunk = chunks.First(c => c.Type == "comment");

        Assert.Equal(0, commentChunk.Metadata.Depth);
    }

    [Fact]
    public void Transform_EmptyBody_SkipsChunk()
    {
        var post = MakePost("p1", "", "");

        var chunks = _transformer.Transform(post, []).ToList();

        Assert.Empty(chunks);
    }

    [Fact]
    public void Transform_PreservesMetadata()
    {
        var post = MakePost("p1", "Title", "Body text here");
        post.Score = 42;
        post.Flair = "Advice";

        var chunks = _transformer.Transform(post, []).ToList();

        Assert.Equal(42, chunks[0].Metadata.Score);
        Assert.Equal("Advice", chunks[0].Metadata.Flair);
    }

    [Fact]
    public void Transform_LongPost_ProducesMultipleChunksWithIndexes()
    {
        var longBody = string.Join(". ", Enumerable.Range(1, 200).Select(i => $"Sentence {i}"));
        var post = MakePost("p1", "Title", longBody);
        var transformer = new Transformer(new CrawlerConfig { MaxTokensPerChunk = 100, ChunkOverlapTokens = 10 });

        var chunks = transformer.Transform(post, []).ToList();

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(chunks.Count, c.Metadata.TotalChunks));
        Assert.Equal(0, chunks[0].Metadata.ChunkIndex);
        Assert.Equal(1, chunks[1].Metadata.ChunkIndex);
    }

    [Fact]
    public void Transform_Post_ContentStartsWithTitle()
    {
        var post = MakePost("p1", "ATS Resume Tips", "Here is some body advice.");

        var chunks = _transformer.Transform(post, []).ToList();

        Assert.NotEmpty(chunks);
        Assert.Contains("ATS Resume Tips", chunks[0].Content);
    }

    [Fact]
    public void Transform_Post_TitleStoredInMetadata()
    {
        var post = MakePost("p1", "My Post Title", "Some body text here.");

        var chunks = _transformer.Transform(post, []).ToList();

        Assert.All(chunks, c => Assert.Equal("My Post Title", c.Metadata.PostTitle));
    }

    [Fact]
    public void Transform_ShortComment_ProducesExactlyOneChunk()
    {
        var post = MakePost("p1", "Title", "Body");
        var comment = MakeComment("c1", "t3_p1", "A short comment that is well under the token limit.");

        var chunks = _transformer.Transform(post, [comment]).ToList();
        var commentChunks = chunks.Where(c => c.Type == "comment").ToList();

        Assert.Single(commentChunks);
    }

    [Fact]
    public void Transform_LongComment_SplitsWithChunkIndexes()
    {
        var longBody = string.Join(". ", Enumerable.Range(1, 200).Select(i => $"Comment sentence {i}"));
        var post = MakePost("p1", "Title", "Body");
        var comment = MakeComment("c1", "t3_p1", longBody);
        var transformer = new Transformer(new CrawlerConfig { MaxTokensPerChunk = 100, ChunkOverlapTokens = 10 });

        var chunks = transformer.Transform(post, [comment]).ToList();
        var commentChunks = chunks.Where(c => c.Type == "comment").ToList();

        Assert.True(commentChunks.Count > 1);
        Assert.Equal(0, commentChunks[0].Metadata.ChunkIndex);
        Assert.Equal(1, commentChunks[1].Metadata.ChunkIndex);
    }

    [Fact]
    public void Transform_Comment_InheritsPostTitleInMetadata()
    {
        var post = MakePost("p1", "My Post Title", "Body text");
        var comment = MakeComment("c1", "t3_p1", "A comment on the post.");

        var chunks = _transformer.Transform(post, [comment]).ToList();
        var commentChunk = chunks.First(c => c.Type == "comment");

        Assert.Equal("My Post Title", commentChunk.Metadata.PostTitle);
    }

    [Fact]
    public void Transform_Timestamp_IsIso8601()
    {
        var post = MakePost("p1", "Title", "Body text");
        var comment = MakeComment("c1", "t3_p1", "A comment");

        var chunks = _transformer.Transform(post, [comment]).ToList();

        Assert.All(chunks, c => Assert.True(
            DateTimeOffset.TryParse(c.Timestamp, out _),
            $"Timestamp '{c.Timestamp}' is not valid ISO 8601"));
    }

    private static RedditPost MakePost(string id, string title, string body) => new()
    {
        Id = id,
        Title = title,
        SelfText = body,
        Author = "testuser",
        Score = 1,
        Subreddit = "test",
        CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    private static RedditPost MakeComment(string id, string parentId, string body) => new()
    {
        Id = id,
        ParentId = parentId,
        Body = body,
        Author = "commenter",
        Score = 1,
        Subreddit = "test",
        CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
}
