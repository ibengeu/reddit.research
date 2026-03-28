using RedditCrawler.Configuration;

namespace RedditCrawler.Services;

/// <summary>
/// Proxy IChatService that delegates to the active provider at call time.
/// This allows the provider and model to be switched from the Settings UI
/// without restarting the application — RagConfig is mutated in place by the UI.
/// </summary>
public sealed class ChatServiceFactory : IChatService
{
    private readonly OllamaChatService _ollama;
    private readonly OpenRouterChatService _openRouter;
    private readonly RagConfig _ragConfig;

    public ChatServiceFactory(
        OllamaChatService ollama,
        OpenRouterChatService openRouter,
        RagConfig ragConfig)
    {
        _ollama = ollama;
        _openRouter = openRouter;
        _ragConfig = ragConfig;
    }

    public IAsyncEnumerable<string> AskStreamAsync(
        string question, int topK, IReadOnlyList<ChatTurn>? history = null, CancellationToken ct = default) =>
        _ragConfig.ChatProvider == ChatProvider.OpenRouter
            ? _openRouter.AskStreamAsync(question, topK, history, ct)
            : _ollama.AskStreamAsync(question, topK, history, ct);
}
