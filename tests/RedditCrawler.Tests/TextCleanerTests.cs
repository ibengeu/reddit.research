using RedditCrawler.Services;

namespace RedditCrawler.Tests;

public class TextCleanerTests
{
    [Theory]
    [InlineData("[click here](https://example.com)", "click here")]
    [InlineData("**bold text**", "bold text")]
    [InlineData("*italic*", "italic")]
    [InlineData("~~struck~~", "struck")]
    [InlineData("`code`", "code")]
    public void Clean_RemovesMarkdownFormatting_ReturnsPlainText(string input, string expected)
    {
        Assert.Equal(expected, TextCleaner.Clean(input));
    }

    [Fact]
    public void Clean_DecodesHtmlEntities()
    {
        Assert.Equal("Tom & Jerry < Bob > Alice", TextCleaner.Clean("Tom &amp; Jerry &lt; Bob &gt; Alice"));
    }

    [Fact]
    public void Clean_CollapsesWhitespace()
    {
        var input = "line one\n\n\n\nline two   three";
        var result = TextCleaner.Clean(input);

        Assert.DoesNotContain("\n\n\n", result);
        Assert.DoesNotContain("   ", result);
    }

    [Fact]
    public void Clean_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal("", TextCleaner.Clean(""));
        Assert.Equal("", TextCleaner.Clean("   "));
        Assert.Equal("", TextCleaner.Clean(null!));
    }

    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var chunks = TextCleaner.ChunkText("Hello world.", 500);
        Assert.Single(chunks);
        Assert.Equal("Hello world.", chunks[0]);
    }

    [Fact]
    public void ChunkText_LongText_SplitsWithinTokenLimit()
    {
        var text = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Sentence number {i}"));
        var chunks = TextCleaner.ChunkText(text, 100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(TextCleaner.EstimateTokens(chunk) <= 110)); // small margin
    }

    [Fact]
    public void ChunkText_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(TextCleaner.ChunkText("", 500));
        Assert.Empty(TextCleaner.ChunkText("   ", 500));
    }

    [Fact]
    public void ChunkText_WithOverlap_AdjacentChunksShareContext()
    {
        // Build text long enough to force at least 3 chunks at 100 tokens
        var text = string.Join(". ", Enumerable.Range(1, 120).Select(i => $"Sentence number {i}"));
        var chunks = TextCleaner.ChunkText(text, 100, overlapTokens: 40);

        Assert.True(chunks.Count >= 2, "Should produce multiple chunks");

        // Every chunk after the first should start with text from the previous chunk's tail
        for (var i = 1; i < chunks.Count; i++)
        {
            // At least some word from the previous chunk appears at the start of the next
            var prevWords = chunks[i - 1].Split(' ').TakeLast(10).ToHashSet();
            var nextStart = string.Join(" ", chunks[i].Split(' ').Take(10));
            Assert.True(prevWords.Any(w => nextStart.Contains(w)),
                $"Chunk {i} should begin with overlap content from chunk {i - 1}");
        }
    }

    [Fact]
    public void ChunkText_WithOverlap_EachChunkRemainsWithinLimit()
    {
        var text = string.Join(". ", Enumerable.Range(1, 120).Select(i => $"Sentence number {i}"));
        var chunks = TextCleaner.ChunkText(text, 100, overlapTokens: 40);

        // Allow a small margin for sentence boundary rounding
        Assert.All(chunks, chunk => Assert.True(TextCleaner.EstimateTokens(chunk) <= 115,
            $"Chunk exceeded token limit: '{chunk[..Math.Min(50, chunk.Length)]}...'"));
    }

    [Fact]
    public void ChunkText_ZeroOverlap_BehavesLikeOriginal()
    {
        var text = string.Join(". ", Enumerable.Range(1, 100).Select(i => $"Sentence number {i}"));
        var withoutOverlap = TextCleaner.ChunkText(text, 100, overlapTokens: 0);
        var legacy = TextCleaner.ChunkText(text, 100);

        Assert.Equal(legacy.Count, withoutOverlap.Count);
    }

    [Fact]
    public void EstimateTokens_ReturnsApproximateCount()
    {
        // ~4 chars per token
        Assert.Equal(0, TextCleaner.EstimateTokens(""));
        Assert.Equal(3, TextCleaner.EstimateTokens("Hello World")); // 11 chars / 4 ≈ 3
    }
}
