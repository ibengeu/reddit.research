using FluentValidation;

namespace RedditCrawler.Configuration;

public sealed class OpenRouterConfig
{
    public bool Enabled { get; set; } = false;
    // OWASP A02:2025 – never hardcode secrets; loaded from env var / secret manager
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string Model { get; set; } = "deepseek/deepseek-chat";
}

public sealed class OpenRouterConfigValidator : AbstractValidator<OpenRouterConfig>
{
    public OpenRouterConfigValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.ApiKey).NotEmpty()
                .WithMessage("OpenRouter API key is required when OpenRouter is enabled.");
            RuleFor(x => x.BaseUrl).NotEmpty();
            RuleFor(x => x.Model).NotEmpty();
        });
    }
}
