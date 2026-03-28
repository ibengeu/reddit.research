using FluentValidation;

namespace RedditCrawler.Configuration;

public sealed class VectorConfig
{
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=reddit;Username=reddit;Password=reddit";
    public string TableName { get; set; } = "reddit_chunks";
    public int VectorSize { get; set; } = 768;
}

public sealed class VectorConfigValidator : AbstractValidator<VectorConfig>
{
    public VectorConfigValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.ConnectionString).NotEmpty();
            RuleFor(x => x.TableName).NotEmpty();
            RuleFor(x => x.VectorSize).GreaterThan(0);
        });
    }
}
