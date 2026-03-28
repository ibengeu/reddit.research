using FluentValidation;

namespace RedditCrawler.Configuration;

public enum ChatProvider { Ollama, OpenRouter }

public sealed class RagConfig
{
    public string? Question { get; set; }
    public int TopK { get; set; } = 5;
    public string ChatModel { get; set; } = "llama3";
    // Retrieve topK × RetrievalMultiplier candidates then rerank, keeping top-K for generation
    public int RetrievalMultiplier { get; set; } = 3;
    public ChatProvider ChatProvider { get; set; } = ChatProvider.Ollama;

    public bool IsAskMode => !string.IsNullOrWhiteSpace(Question);
}

public sealed class RagConfigValidator : AbstractValidator<RagConfig>
{
    public RagConfigValidator()
    {
        When(x => x.IsAskMode, () =>
        {
            RuleFor(x => x.TopK).InclusiveBetween(1, 50);
            RuleFor(x => x.ChatModel).NotEmpty();
            RuleFor(x => x.RetrievalMultiplier).InclusiveBetween(1, 10);
        });
    }
}
