using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedditCrawler;
using RedditCrawler.Configuration;
using RedditCrawler.Services;
using ChatProvider = RedditCrawler.Configuration.ChatProvider;

var (config, ollamaConfig, vectorConfig, ragConfig, openRouterConfig) = ParseArgs(args);

// Ask mode requires embedding + vector store
if (ragConfig.IsAskMode)
{
    ollamaConfig.Enabled = true;
    vectorConfig.Enabled = true;
}

var services = new ServiceCollection()
    .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
    .AddRedditCrawlerCore(config, ollamaConfig, vectorConfig, ragConfig, openRouterConfig);

var sp = services.BuildServiceProvider();
var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("RedditCrawler");

// Validate configs
var crawlerResult = sp.GetRequiredService<IValidator<CrawlerConfig>>().Validate(config);
if (!crawlerResult.IsValid)
{
    foreach (var error in crawlerResult.Errors)
        logger.LogError("Config error: {Error}", error.ErrorMessage);
    return 1;
}

var ollamaResult = sp.GetRequiredService<IValidator<OllamaConfig>>().Validate(ollamaConfig);
if (!ollamaResult.IsValid)
{
    foreach (var error in ollamaResult.Errors)
        logger.LogError("Ollama config error: {Error}", error.ErrorMessage);
    return 1;
}

var vectorResult = sp.GetRequiredService<IValidator<VectorConfig>>().Validate(vectorConfig);
if (!vectorResult.IsValid)
{
    foreach (var error in vectorResult.Errors)
        logger.LogError("Vector config error: {Error}", error.ErrorMessage);
    return 1;
}

var ragResult = sp.GetRequiredService<IValidator<RagConfig>>().Validate(ragConfig);
if (!ragResult.IsValid)
{
    foreach (var error in ragResult.Errors)
        logger.LogError("RAG config error: {Error}", error.ErrorMessage);
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// --- ASK MODE ---
if (ragConfig.IsAskMode)
{
    var chat = sp.GetRequiredService<IChatService>();
    logger.LogInformation("RAG query mode (model: {Model}, top-k: {K})", ragConfig.ChatModel, ragConfig.TopK);

    try
    {
        Console.WriteLine();
        await foreach (var token in chat.AskStreamAsync(ragConfig.Question!, ragConfig.TopK, ct: cts.Token))
        {
            if (token.StartsWith(RedditCrawler.Services.ChatStreamTokens.SourcesSentinel))
                Console.WriteLine("\n" + token[RedditCrawler.Services.ChatStreamTokens.SourcesSentinel.Length..]);
            else if (token.StartsWith(RedditCrawler.Services.ChatStreamTokens.ErrorSentinel))
                Console.Error.WriteLine("\nError: " + token[RedditCrawler.Services.ChatStreamTokens.ErrorSentinel.Length..]);
            else
                Console.Write(token);
        }
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Query failed");
        return 1;
    }

    await sp.DisposeAsync();
    return 0;
}

// --- CRAWL MODE ---
var crawler = sp.GetRequiredService<ICrawlerService>();
var transformer = sp.GetRequiredService<ITransformer>();
var storage = sp.GetRequiredService<IStorageService>();
var embedder = sp.GetRequiredService<IEmbeddingService>();
var enricher = sp.GetRequiredService<IChunkEnricher>();
var vectorStore = sp.GetRequiredService<IVectorStoreService>();

if (vectorStore.IsEnabled)
    await vectorStore.EnsureCollectionAsync(cts.Token);

if (embedder.IsEnabled)
    logger.LogInformation("Ollama embeddings enabled (model: {Model})", ollamaConfig.Model);
if (vectorStore.IsEnabled)
    logger.LogInformation("PostgreSQL/pgvector ingestion enabled (table: {Table})", vectorConfig.TableName);

var postsProcessed = 0;

try
{
    foreach (var subreddit in config.Subreddits)
    {
        logger.LogInformation("Crawling r/{Sub} via Arctic Shift API...", subreddit);

        await foreach (var (post, comments) in crawler.CrawlAsync(subreddit, cts.Token))
        {
            var chunks = transformer.Transform(post, comments)
                .Where(c => config.MinScore == 0 || c.Metadata.Score >= config.MinScore);
            foreach (var chunk in chunks)
            {
                if (embedder.IsEnabled)
                {
                    // Enrich text for embedding only; chunk.Content (original) is stored unchanged.
                    var textToEmbed = await enricher.EnrichAsync(chunk, cts.Token);
                    chunk.Embedding = await embedder.GenerateEmbeddingAsync(textToEmbed, cts.Token);
                }

                if (vectorStore.IsEnabled && chunk.Embedding is not null)
                    await vectorStore.UpsertChunkAsync(chunk, cts.Token);

                // Strip embedding before writing to JSONL — vectors live in PostgreSQL,
                // serialising ~3KB of floats per chunk to disk serves no purpose.
                chunk.Embedding = null;
                await storage.WriteChunkAsync(chunk, cts.Token);
            }

            postsProcessed++;
            if (postsProcessed % 10 == 0)
                logger.LogInformation("Progress: {Posts} posts, {Chunks} chunks", postsProcessed, storage.ChunksWritten);
        }
    }
}
catch (OperationCanceledException)
{
    logger.LogWarning("Crawl cancelled by user");
}

logger.LogInformation("Done. Posts: {Posts}, Chunks: {Chunks}", postsProcessed, storage.ChunksWritten);

await ((IAsyncDisposable)storage).DisposeAsync();
await sp.DisposeAsync();
return 0;

static (CrawlerConfig config, OllamaConfig ollama, VectorConfig vector, RagConfig rag, OpenRouterConfig openRouter) ParseArgs(string[] args)
{
    var config = new CrawlerConfig { Subreddits = [] };
    var ollama = new OllamaConfig();
    var vector = new VectorConfig();
    var rag = new RagConfig();
    var openRouter = new OpenRouterConfig();

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i].ToLowerInvariant();
        var next = i + 1 < args.Length ? args[i + 1] : null;

        // Helper: require a value for flags that take an argument
        string RequireNext(string flag)
        {
            if (next is null) throw new ArgumentException($"Missing value for {flag}");
            i++;
            return next;
        }

        int RequireInt(string flag)
        {
            var val = RequireNext(flag);
            if (!int.TryParse(val, out var result))
                throw new ArgumentException($"Invalid integer for {flag}: {val}");
            return result;
        }

        switch (arg)
        {
            case "--subreddit": config.Subreddits.Add(RequireNext(arg)); break;
            case "--limit": config.Limit = RequireInt(arg); break;
            case "--sort": config.Sort = RequireNext(arg); break;
            case "--output": config.OutputPath = RequireNext(arg); break;
            case "--max-tokens": config.MaxTokensPerChunk = RequireInt(arg); break;
            case "--arctic-shift-url": config.ArcticShiftBaseUrl = RequireNext(arg); break;
            case "--min-score": config.MinScore = RequireInt(arg); break;
            case "--enrich": config.EnableContextualEnrichment = true; break;

            case "--embed": ollama.Enabled = true; break;
            case "--embed-model": ollama.Model = RequireNext(arg); break;
            case "--ollama-url": ollama.BaseUrl = RequireNext(arg); break;

            case "--pg-connection": vector.ConnectionString = RequireNext(arg); vector.Enabled = true; break;
            case "--pg-table": vector.TableName = RequireNext(arg); break;

            case "--ask": rag.Question = RequireNext(arg); break;
            case "--top-k": rag.TopK = RequireInt(arg); break;
            case "--chat-model": rag.ChatModel = RequireNext(arg); break;
            case "--retrieval-multiplier": rag.RetrievalMultiplier = RequireInt(arg); break;
            // OWASP A05:2025 – Prefer OPENROUTER_API_KEY env var over CLI to avoid process-list exposure
            case "--openrouter-key":
                openRouter.ApiKey = RequireNext(arg); openRouter.Enabled = true;
                rag.ChatProvider = ChatProvider.OpenRouter; break;
            case "--openrouter-model": openRouter.Model = RequireNext(arg); break;
        }
    }

    // Default subreddit if none specified (and not in ask mode)
    if (config.Subreddits.Count == 0 && !rag.IsAskMode)
    {
        config.Subreddits.Add("ResumesATS");
        Console.Error.WriteLine("Warning: No --subreddit specified, defaulting to ResumesATS");
    }

    // OWASP A05:2025 – Support env var for API key to avoid CLI exposure
    if (string.IsNullOrWhiteSpace(openRouter.ApiKey))
    {
        var envKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            openRouter.ApiKey = envKey;
            openRouter.Enabled = true;
            rag.ChatProvider = ChatProvider.OpenRouter;
        }
    }

    return (config, ollama, vector, rag, openRouter);
}
