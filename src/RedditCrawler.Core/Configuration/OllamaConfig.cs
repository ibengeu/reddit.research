using FluentValidation;

namespace RedditCrawler.Configuration;

public sealed class OllamaConfig
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "nomic-embed-text";
}

public sealed class OllamaConfigValidator : AbstractValidator<OllamaConfig>
{
    public OllamaConfigValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.BaseUrl).NotEmpty();
            RuleFor(x => x.Model).NotEmpty();
        });
    }
}
