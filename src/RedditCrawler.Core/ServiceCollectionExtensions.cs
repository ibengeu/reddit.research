using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedditCrawler.Configuration;
using RedditCrawler.Services;

namespace RedditCrawler;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRedditCrawlerCore(
        this IServiceCollection services,
        CrawlerConfig crawlerConfig,
        OllamaConfig ollamaConfig,
        VectorConfig vectorConfig,
        RagConfig ragConfig,
        OpenRouterConfig openRouterConfig)
    {
        services
            .AddValidatorsFromAssemblyContaining<CrawlerConfigValidator>()
            .AddHttpClient()
            .AddHttpClient("ollama", c =>
            {
                c.BaseAddress = new Uri(ollamaConfig.BaseUrl);
                c.Timeout = TimeSpan.FromMinutes(10);
            })
            .Services
            .AddHttpClient("arctic-shift", c =>
            {
                c.BaseAddress = new Uri(crawlerConfig.ArcticShiftBaseUrl);
                c.DefaultRequestHeaders.UserAgent.ParseAdd("RedditCrawler/1.0 (.NET; LLM Data Pipeline)");
            })
            .Services
            .AddHttpClient("openrouter", c =>
            {
                var baseUrl = openRouterConfig.BaseUrl.TrimEnd('/') + '/';
                c.BaseAddress = new Uri(baseUrl);
                c.Timeout = TimeSpan.FromMinutes(10);
            })
            .Services
            .AddSingleton(crawlerConfig)
            .AddSingleton(ollamaConfig)
            .AddSingleton(vectorConfig)
            .AddSingleton(ragConfig)
            .AddSingleton(openRouterConfig)
            .AddSingleton<IRedditClient>(sp => new ArcticShiftClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("arctic-shift"),
                sp.GetRequiredService<ILogger<ArcticShiftClient>>()))
            .AddSingleton<ICrawlerService, CrawlerService>()
            .AddSingleton<ITransformer, Transformer>()
            .AddSingleton<IStorageService, JsonlStorageService>();

        if (ollamaConfig.Enabled)
            services.AddSingleton<IEmbeddingService>(sp => new OllamaEmbeddingService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
                sp.GetRequiredService<OllamaConfig>(),
                sp.GetRequiredService<ILogger<OllamaEmbeddingService>>()));
        else
            services.AddSingleton<IEmbeddingService, NoOpEmbeddingService>();

        if (crawlerConfig.EnableContextualEnrichment && ollamaConfig.Enabled)
            services.AddSingleton<IChunkEnricher>(sp => new OllamaChunkEnricher(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
                sp.GetRequiredService<OllamaConfig>(),
                sp.GetRequiredService<RagConfig>(),
                sp.GetRequiredService<ILogger<OllamaChunkEnricher>>()));
        else
            services.AddSingleton<IChunkEnricher, NoOpChunkEnricher>();

        if (vectorConfig.Enabled)
            services.AddSingleton<IVectorStoreService, NpgsqlVectorStoreService>();
        else
            services.AddSingleton<IVectorStoreService, NoOpVectorStoreService>();

        // Always register both concrete services + the factory proxy.
        // The factory reads RagConfig.ChatProvider at call time → hot-swappable at runtime.
        services
            .AddSingleton<OllamaChatService>(sp => new OllamaChatService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"),
                sp.GetRequiredService<IEmbeddingService>(),
                sp.GetRequiredService<IVectorStoreService>(),
                sp.GetRequiredService<RagConfig>(),
                sp.GetRequiredService<OllamaConfig>(),
                sp.GetRequiredService<ILogger<OllamaChatService>>()))
            .AddSingleton<OpenRouterChatService>(sp => new OpenRouterChatService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("openrouter"),
                sp.GetRequiredService<IEmbeddingService>(),
                sp.GetRequiredService<IVectorStoreService>(),
                sp.GetRequiredService<RagConfig>(),
                sp.GetRequiredService<OpenRouterConfig>(),
                sp.GetRequiredService<ILogger<OpenRouterChatService>>()))
            .AddSingleton<IChatService, ChatServiceFactory>();

        return services;
    }
}
