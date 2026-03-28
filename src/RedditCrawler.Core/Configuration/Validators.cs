using FluentValidation;

namespace RedditCrawler.Configuration;

public sealed class CrawlerConfigValidator : AbstractValidator<CrawlerConfig>
{
    public CrawlerConfigValidator()
    {
        RuleFor(x => x.Subreddits)
            .NotEmpty().WithMessage("At least one subreddit is required.");

        RuleForEach(x => x.Subreddits)
            .NotEmpty().WithMessage("Subreddit name cannot be blank.");

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 1000);

        RuleFor(x => x.Sort)
            .Must(s => CrawlerConfig.ValidSorts.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", CrawlerConfig.ValidSorts)}");

        RuleFor(x => x.MaxTokensPerChunk)
            .GreaterThanOrEqualTo(50);

        RuleFor(x => x.ChunkOverlapTokens)
            .GreaterThanOrEqualTo(0)
            .Must((cfg, overlap) => overlap < cfg.MaxTokensPerChunk)
            .WithMessage("ChunkOverlapTokens must be less than MaxTokensPerChunk.");

        RuleFor(x => x.ArcticShiftBaseUrl).NotEmpty();

        RuleFor(x => x.MinScore).GreaterThanOrEqualTo(0);
    }
}
