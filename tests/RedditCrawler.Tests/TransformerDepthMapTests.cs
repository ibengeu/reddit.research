using RedditCrawler.Configuration;
using RedditCrawler.Models;
using RedditCrawler.Services;

namespace RedditCrawler.Tests;

public class TransformerDepthMapTests
{
    private readonly Transformer _transformer = new(new CrawlerConfig { MaxTokensPerChunk = 500 });

    [Fact]
    public void Transform_OutOfOrderComments_DepthCorrect()
    {
        var post = MakePost("p1", "Title", "Body");
        var child = MakeComment("c2", "t1_c1", "Child listed first");
        var parent = MakeComment("c1", "t3_p1", "Parent listed second");

        var chunks = _transformer.Transform(post, [child, parent]).ToList();
        var childChunk = chunks.First(c => c.Id.Contains("c2"));

        Assert.Equal(1, childChunk.Metadata.Depth);
    }

    [Fact]
    public void Transform_ThreeLevelsDeep_AllDepthsCorrect()
    {
        var post = MakePost("p1", "Title", "Body");
        var l1 = MakeComment("c1", "t3_p1", "Level 1");
        var l2 = MakeComment("c2", "t1_c1", "Level 2");
        var l3 = MakeComment("c3", "t1_c2", "Level 3");

        var chunks = _transformer.Transform(post, [l1, l2, l3]).ToList();

        Assert.Equal(0, chunks.First(c => c.Id.Contains("c1")).Metadata.Depth);
        Assert.Equal(1, chunks.First(c => c.Id.Contains("c2")).Metadata.Depth);
        Assert.Equal(2, chunks.First(c => c.Id.Contains("c3")).Metadata.Depth);
    }

    [Fact]
    public void Transform_ThreeLevelsDeep_OutOfOrder_AllDepthsCorrect()
    {
        var post = MakePost("p1", "Title", "Body");
        // Deepest first, then mid, then top-level — worst-case ordering
        var l3 = MakeComment("c3", "t1_c2", "Level 3");
        var l2 = MakeComment("c2", "t1_c1", "Level 2");
        var l1 = MakeComment("c1", "t3_p1", "Level 1");

        var chunks = _transformer.Transform(post, [l3, l2, l1]).ToList();

        Assert.Equal(0, chunks.First(c => c.Id.Contains("c1")).Metadata.Depth);
        Assert.Equal(1, chunks.First(c => c.Id.Contains("c2")).Metadata.Depth);
        Assert.Equal(2, chunks.First(c => c.Id.Contains("c3")).Metadata.Depth);
    }

    [Fact]
    public void Transform_OrphanComment_HasDepthOne()
    {
        var post = MakePost("p1", "Title", "Body");
        // Parent ID references a comment not in the list — unknown parent defaults to depth 0+1=1
        var orphan = MakeComment("c1", "t1_unknown", "Orphan comment");

        var chunks = _transformer.Transform(post, [orphan]).ToList();
        var orphanChunk = chunks.First(c => c.Id.Contains("c1"));

        Assert.Equal(1, orphanChunk.Metadata.Depth);
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
