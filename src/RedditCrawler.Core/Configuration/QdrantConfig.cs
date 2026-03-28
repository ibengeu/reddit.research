using FluentValidation;

namespace RedditCrawler.Configuration;

public sealed class QdrantConfig
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int GrpcPort { get; set; } = 6334;
    public int RestPort { get; set; } = 6333;
    public string CollectionName { get; set; } = "reddit_chunks";
    public int VectorSize { get; set; } = 768;
}

public sealed class QdrantConfigValidator : AbstractValidator<QdrantConfig>
{
    public QdrantConfigValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.Host).NotEmpty();
            RuleFor(x => x.GrpcPort).InclusiveBetween(1, 65535);
            RuleFor(x => x.RestPort).InclusiveBetween(1, 65535);
            RuleFor(x => x.CollectionName).NotEmpty();
            RuleFor(x => x.VectorSize).GreaterThan(0);
        });
    }
}
