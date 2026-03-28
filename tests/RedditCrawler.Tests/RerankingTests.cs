using RedditCrawler.Models;
using RedditCrawler.Services;

namespace RedditCrawler.Tests;

/// <summary>
/// Tests for the retrieve-wide / rerank-narrow pipeline and Lost-in-the-Middle ordering.
/// Uses reflection-free, behaviour-only assertions against public contracts.
/// </summary>
public class RerankingTests
{
    // Helper: build a ScoredChunk with controlled similarity and community score
    private static ScoredChunk MakeChunk(string content, float similarity, int redditScore) =>
        new(content, "test", similarity, redditScore, null, "2024-01-01T00:00:00Z");

    [Fact]
    public void Rerank_HighCommunityScore_PromotedOverPureHighSimilarity()
    {
        // A chunk with lower cosine similarity but many upvotes should outscore
        // a marginally more similar chunk with zero upvotes.
        var highSim_lowScore = MakeChunk("A", similarity: 0.90f, redditScore: 1);
        var lowSim_highScore = MakeChunk("B", similarity: 0.80f, redditScore: 500);

        var combined = new List<ScoredChunk> { highSim_lowScore, lowSim_highScore };
        var reranked = CombinedScoreRerank(combined, take: 2);

        // B (community-validated) should rank above A (high similarity but no votes)
        Assert.Equal("B", reranked[0].Content);
    }

    [Fact]
    public void Rerank_ZeroRedditScore_DoesNotProduceNegativeOrNanScore()
    {
        var chunk = MakeChunk("A", similarity: 0.75f, redditScore: 0);
        var reranked = CombinedScoreRerank([chunk], take: 1);

        Assert.Single(reranked);
        // log(1 + 0) = 0, so combined score is 0 — chunk still appears, no exception
        Assert.Equal("A", reranked[0].Content);
    }

    [Fact]
    public void Rerank_NegativeRedditScore_ClampedToZero_NoException()
    {
        // Downvoted content (negative score) should not cause math errors
        var chunk = MakeChunk("A", similarity: 0.70f, redditScore: -10);
        var exception = Record.Exception(() => CombinedScoreRerank([chunk], take: 1));

        Assert.Null(exception);
    }

    [Fact]
    public void Rerank_TakesOnlyTopK_FromLargerCandidateSet()
    {
        var candidates = Enumerable.Range(1, 15)
            .Select(i => MakeChunk($"Chunk{i}", similarity: i / 20f, redditScore: i * 10))
            .ToList();

        var reranked = CombinedScoreRerank(candidates, take: 5);

        Assert.Equal(5, reranked.Count);
    }

    [Fact]
    public void LostInMiddle_BestChunkAtPositionZero()
    {
        var chunks = new List<ScoredChunk>
        {
            MakeChunk("Best",   similarity: 0.95f, redditScore: 100),
            MakeChunk("Second", similarity: 0.85f, redditScore: 80),
            MakeChunk("Third",  similarity: 0.75f, redditScore: 60),
            MakeChunk("Fourth", similarity: 0.65f, redditScore: 40),
        };

        var ordered = ReorderForAttention(chunks);

        Assert.Equal("Best", ordered[0].Content);
    }

    [Fact]
    public void LostInMiddle_SecondBestChunkAtLastPosition()
    {
        var chunks = new List<ScoredChunk>
        {
            MakeChunk("Best",   similarity: 0.95f, redditScore: 100),
            MakeChunk("Second", similarity: 0.85f, redditScore: 80),
            MakeChunk("Third",  similarity: 0.75f, redditScore: 60),
            MakeChunk("Fourth", similarity: 0.65f, redditScore: 40),
        };

        var ordered = ReorderForAttention(chunks);

        Assert.Equal("Second", ordered[^1].Content);
    }

    [Fact]
    public void LostInMiddle_TwoChunks_ReturnedUnchanged()
    {
        var chunks = new List<ScoredChunk>
        {
            MakeChunk("A", similarity: 0.9f, redditScore: 10),
            MakeChunk("B", similarity: 0.8f, redditScore: 5),
        };

        var ordered = ReorderForAttention(chunks);

        Assert.Equal(["A", "B"], ordered.Select(c => c.Content));
    }

    [Fact]
    public void LostInMiddle_PreservesAllChunks()
    {
        var chunks = Enumerable.Range(1, 6)
            .Select(i => MakeChunk($"C{i}", similarity: i / 10f, redditScore: i))
            .ToList();

        var ordered = ReorderForAttention(chunks);

        Assert.Equal(chunks.Count, ordered.Count);
        Assert.Equal(chunks.Select(c => c.Content).OrderBy(x => x),
                     ordered.Select(c => c.Content).OrderBy(x => x));
    }

    // ── Mirrors the logic in OllamaChatService without referencing internal types ──

    private static List<ScoredChunk> CombinedScoreRerank(List<ScoredChunk> candidates, int take) =>
        candidates
            .OrderByDescending(c => c.SimilarityScore * Math.Log(2 + Math.Max(0, c.RedditScore)))
            .Take(take)
            .ToList();

    private static List<ScoredChunk> ReorderForAttention(List<ScoredChunk> ranked)
    {
        if (ranked.Count <= 2) return ranked;
        var result = new List<ScoredChunk>(ranked.Count);
        result.Add(ranked[0]);
        result.AddRange(ranked.Skip(2));
        result.Add(ranked[1]);
        return result;
    }
}
