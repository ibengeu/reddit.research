using FluentValidation.TestHelper;
using RedditCrawler.Configuration;

namespace RedditCrawler.Tests;

public class CrawlerConfigValidatorTests
{
    private readonly CrawlerConfigValidator _validator = new();

    [Fact]
    public void ValidConfig_PassesValidation()
    {
        var config = new CrawlerConfig();
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptySubreddits_FailsValidation()
    {
        var config = new CrawlerConfig { Subreddits = [] };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.Subreddits);
    }

    [Fact]
    public void BlankSubredditEntry_FailsValidation()
    {
        var config = new CrawlerConfig { Subreddits = ["csharp", ""] };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor("Subreddits[1]");
    }

    [Fact]
    public void MultipleValidSubreddits_PassesValidation()
    {
        var config = new CrawlerConfig { Subreddits = ["csharp", "dotnet", "programming"] };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidLimit_FailsValidation(int limit)
    {
        var config = new CrawlerConfig { Limit = limit };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.Limit);
    }

    [Fact]
    public void InvalidSort_FailsValidation()
    {
        var config = new CrawlerConfig { Sort = "invalid" };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.Sort);
    }

    [Fact]
    public void TooSmallMaxTokens_FailsValidation()
    {
        var config = new CrawlerConfig { MaxTokensPerChunk = 10 };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.MaxTokensPerChunk);
    }

    [Fact]
    public void EmptyArcticShiftUrl_FailsValidation()
    {
        var config = new CrawlerConfig { ArcticShiftBaseUrl = "" };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.ArcticShiftBaseUrl);
    }

    [Fact]
    public void NegativeMinScore_FailsValidation()
    {
        var config = new CrawlerConfig { MinScore = -1 };
        _validator.TestValidate(config).ShouldHaveValidationErrorFor(x => x.MinScore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void NonNegativeMinScore_PassesValidation(int score)
    {
        var config = new CrawlerConfig { MinScore = score };
        _validator.TestValidate(config).ShouldNotHaveValidationErrorFor(x => x.MinScore);
    }
}

public class QdrantConfigValidatorTests
{
    private readonly QdrantConfigValidator _validator = new();

    [Fact]
    public void DisabledConfig_PassesValidation()
    {
        var config = new QdrantConfig { Enabled = false, Host = "" };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EnabledWithDefaults_PassesValidation()
    {
        var config = new QdrantConfig { Enabled = true };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EnabledWithEmptyHost_FailsValidation()
    {
        var config = new QdrantConfig { Enabled = true, Host = "" };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.Host);
    }

    [Fact]
    public void EnabledWithEmptyCollection_FailsValidation()
    {
        var config = new QdrantConfig { Enabled = true, CollectionName = "" };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.CollectionName);
    }
}

public class RagConfigValidatorTests
{
    private readonly RagConfigValidator _validator = new();

    [Fact]
    public void NoQuestion_PassesValidation()
    {
        var config = new RagConfig();
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ValidQuestion_PassesValidation()
    {
        var config = new RagConfig { Question = "What is async?" };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void QuestionWithInvalidTopK_FailsValidation()
    {
        var config = new RagConfig { Question = "test", TopK = 0 };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.TopK);
    }

    [Fact]
    public void QuestionWithEmptyChatModel_FailsValidation()
    {
        var config = new RagConfig { Question = "test", ChatModel = "" };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.ChatModel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void QuestionWithInvalidRetrievalMultiplier_FailsValidation(int multiplier)
    {
        var config = new RagConfig { Question = "test", RetrievalMultiplier = multiplier };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.RetrievalMultiplier);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void QuestionWithValidRetrievalMultiplier_PassesValidation(int multiplier)
    {
        var config = new RagConfig { Question = "test", RetrievalMultiplier = multiplier };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveValidationErrorFor(x => x.RetrievalMultiplier);
    }
}

public class ChunkOverlapValidatorTests
{
    private readonly CrawlerConfigValidator _validator = new();

    [Fact]
    public void OverlapLessThanMaxTokens_PassesValidation()
    {
        var config = new CrawlerConfig { MaxTokensPerChunk = 512, ChunkOverlapTokens = 50 };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveValidationErrorFor(x => x.ChunkOverlapTokens);
    }

    [Fact]
    public void OverlapEqualToMaxTokens_FailsValidation()
    {
        var config = new CrawlerConfig { MaxTokensPerChunk = 512, ChunkOverlapTokens = 512 };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.ChunkOverlapTokens);
    }

    [Fact]
    public void NegativeOverlap_FailsValidation()
    {
        var config = new CrawlerConfig { MaxTokensPerChunk = 512, ChunkOverlapTokens = -1 };
        var result = _validator.TestValidate(config);
        result.ShouldHaveValidationErrorFor(x => x.ChunkOverlapTokens);
    }

    [Fact]
    public void ZeroOverlap_PassesValidation()
    {
        var config = new CrawlerConfig { MaxTokensPerChunk = 512, ChunkOverlapTokens = 0 };
        var result = _validator.TestValidate(config);
        result.ShouldNotHaveValidationErrorFor(x => x.ChunkOverlapTokens);
    }
}
