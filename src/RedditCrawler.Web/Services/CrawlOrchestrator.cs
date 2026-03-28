using System.Diagnostics;
using RedditCrawler.Configuration;
using RedditCrawler.Services;

namespace RedditCrawler.Web.Services;

public sealed class CrawlOrchestrator
{
    private readonly ICrawlerService _crawler;
    private readonly ITransformer _transformer;
    private readonly IEmbeddingService _embedder;
    private readonly IChunkEnricher _enricher;
    private readonly IStorageService _storage;
    private readonly IVectorStoreService _vectorStore;
    private readonly ILogger<CrawlOrchestrator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }
    private int _postsProcessed;
    private int _chunksGenerated;
    private int _chunksFiltered;
    private int _embeddingsGenerated;
    private int _vectorUpserts;

    public int PostsProcessed => _postsProcessed;
    public int ChunksGenerated => _chunksGenerated;
    public int ChunksFiltered => _chunksFiltered;
    public int EmbeddingsGenerated => _embeddingsGenerated;
    public int VectorUpserts => _vectorUpserts;
    public string? CurrentSubreddit { get; private set; }

    private readonly List<string> _logMessages = [];
    private readonly object _logLock = new();

    /// <summary>Returns a point-in-time snapshot of the log, safe to enumerate from any thread.</summary>
    public IReadOnlyList<string> GetLogSnapshot()
    {
        lock (_logLock)
            return _logMessages.ToList();
    }

    public event Action? OnStateChanged;

    public CrawlOrchestrator(
        ICrawlerService crawler,
        ITransformer transformer,
        IEmbeddingService embedder,
        IChunkEnricher enricher,
        IStorageService storage,
        IVectorStoreService vectorStore,
        ILogger<CrawlOrchestrator> logger)
    {
        _crawler = crawler;
        _transformer = transformer;
        _embedder = embedder;
        _enricher = enricher;
        _storage = storage;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task StartCrawlAsync(List<string> subreddits, int limit, string sort, int minScore = 0)
    {
        if (!_gate.Wait(0))
        {
            _logger.LogWarning("Crawl already running, ignoring start request");
            return;
        }

        var totalSw = Stopwatch.StartNew();

        try
        {
            _cts = new CancellationTokenSource();
            IsRunning = true;
            Interlocked.Exchange(ref _postsProcessed, 0);
            Interlocked.Exchange(ref _chunksGenerated, 0);
            Interlocked.Exchange(ref _chunksFiltered, 0);
            Interlocked.Exchange(ref _embeddingsGenerated, 0);
            Interlocked.Exchange(ref _vectorUpserts, 0);
            lock (_logLock)
                _logMessages.Clear();
            NotifyStateChanged();

            _logger.LogInformation("Crawl started — subreddits: {Subreddits}, limit: {Limit}, sort: {Sort}, embed: {Embed}, vectorStore: {VectorStore}",
                string.Join(", ", subreddits), limit, sort, _embedder.IsEnabled, _vectorStore.IsEnabled);
            AddLog($"Starting crawl: {string.Join(", ", subreddits.Select(s => "r/" + s))}");

            if (_vectorStore.IsEnabled)
            {
                _logger.LogInformation("Ensuring vector store table exists");
                await _vectorStore.EnsureCollectionAsync(_cts.Token);
            }

            foreach (var subreddit in subreddits)
            {
                CurrentSubreddit = subreddit;
                var subSw = Stopwatch.StartNew();
                var subPosts = 0;
                var subChunks = 0;

                _logger.LogInformation("Crawling r/{Subreddit}", subreddit);
                AddLog($"Crawling r/{subreddit}...");

                await foreach (var (post, comments) in _crawler.CrawlAsync(subreddit, limit, _cts.Token))
                {
                    var chunks = _transformer.Transform(post, comments).ToList();
                    if (minScore > 0)
                    {
                        var before = chunks.Count;
                        chunks = chunks.Where(c => c.Metadata.Score >= minScore).ToList();
                        Interlocked.Add(ref _chunksFiltered, before - chunks.Count);
                    }
                    var title = post.Title is { Length: > 0 } t ? t[..Math.Min(60, t.Length)] : "(no title)";
                    _logger.LogDebug("Post {PostId} ({Title}) => {ChunkCount} chunks, {CommentCount} comments",
                        post.Id, title, chunks.Count, comments.Count);

                    foreach (var chunk in chunks)
                    {
                        if (_embedder.IsEnabled)
                        {
                            // Enrich text for embedding only; chunk.Content (original) is stored unchanged.
                            var textToEmbed = await _enricher.EnrichAsync(chunk, _cts.Token);
                            chunk.Embedding = await _embedder.GenerateEmbeddingAsync(textToEmbed, _cts.Token);
                            if (chunk.Embedding is not null)
                                Interlocked.Increment(ref _embeddingsGenerated);
                        }

                        if (_vectorStore.IsEnabled && chunk.Embedding is not null)
                        {
                            await _vectorStore.UpsertChunkAsync(chunk, _cts.Token);
                            Interlocked.Increment(ref _vectorUpserts);
                        }

                        // Strip embedding before writing to JSONL — vectors live in PostgreSQL,
                        // serialising ~3KB of floats per chunk to disk serves no purpose.
                        chunk.Embedding = null;
                        await _storage.WriteChunkAsync(chunk, _cts.Token);

                        Interlocked.Increment(ref _chunksGenerated);
                        subChunks++;
                    }

                    Interlocked.Increment(ref _postsProcessed);
                    subPosts++;

                    if (subPosts % 10 == 0)
                    {
                        _logger.LogInformation("r/{Subreddit} progress: {Posts} posts, {Chunks} chunks, {Elapsed}s elapsed",
                            subreddit, subPosts, subChunks, (int)subSw.Elapsed.TotalSeconds);
                        AddLog($"r/{subreddit}: {subPosts} posts, {subChunks} chunks ({(int)subSw.Elapsed.TotalSeconds}s)");
                    }

                    NotifyStateChanged();
                }

                subSw.Stop();
                _logger.LogInformation("Finished r/{Subreddit}: {Posts} posts, {Chunks} chunks, {Embeddings} embeddings, {Upserts} upserts in {Elapsed:0.0}s",
                    subreddit, subPosts, subChunks, EmbeddingsGenerated, VectorUpserts, subSw.Elapsed.TotalSeconds);
                AddLog($"Done r/{subreddit}: {subPosts} posts, {subChunks} chunks in {subSw.Elapsed.TotalSeconds:0.0}s");
            }

            totalSw.Stop();
            _logger.LogInformation("Crawl complete — total posts: {Posts}, chunks: {Chunks}, filtered: {Filtered}, embeddings: {Embeddings}, upserts: {Upserts}, duration: {Elapsed:0.0}s",
                PostsProcessed, ChunksGenerated, ChunksFiltered, EmbeddingsGenerated, VectorUpserts, totalSw.Elapsed.TotalSeconds);
            var filteredNote = ChunksFiltered > 0 ? $", {ChunksFiltered} filtered" : "";
            AddLog($"Crawl complete: {PostsProcessed} posts, {ChunksGenerated} chunks{filteredNote}, {EmbeddingsGenerated} embeddings in {totalSw.Elapsed.TotalSeconds:0.0}s");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Crawl cancelled after {Posts} posts, {Chunks} chunks", PostsProcessed, ChunksGenerated);
            AddLog($"Crawl cancelled ({PostsProcessed} posts, {ChunksGenerated} chunks processed)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Crawl failed after {Posts} posts, {Chunks} chunks", PostsProcessed, ChunksGenerated);
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            CurrentSubreddit = null;
            _cts?.Dispose();
            _cts = null;
            _gate.Release();
            NotifyStateChanged();
        }
    }

    public void Cancel()
    {
        _logger.LogInformation("Crawl cancellation requested");
        _cts?.Cancel();
    }

    private void AddLog(string message)
    {
        lock (_logLock)
        {
            _logMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_logMessages.Count > 200)
                _logMessages.RemoveAt(0);
        }
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnStateChanged?.Invoke();
}
