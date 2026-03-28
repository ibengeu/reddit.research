using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RedditCrawler.Configuration;

namespace RedditCrawler.Services;

/// <summary>
/// IChatService backed by OpenRouter (OpenAI-compatible SSE streaming).
/// Embeddings still run locally via Ollama — OpenRouter is chat-generation only.
/// Reads the active model from RagConfig at call time, so the model is hot-swappable
/// from the Settings UI without restarting the app.
/// </summary>
public sealed class OpenRouterChatService : IChatService
{
    private readonly HttpClient _http;
    private readonly IEmbeddingService _embedder;
    private readonly IVectorStoreService _vectorStore;
    private readonly RagConfig _ragConfig;
    private readonly OpenRouterConfig _openRouterConfig;
    private readonly ILogger<OpenRouterChatService> _logger;

    public OpenRouterChatService(
        HttpClient http,
        IEmbeddingService embedder,
        IVectorStoreService vectorStore,
        RagConfig ragConfig,
        OpenRouterConfig openRouterConfig,
        ILogger<OpenRouterChatService> logger)
    {
        _http = http;
        _embedder = embedder;
        _vectorStore = vectorStore;
        _ragConfig = ragConfig;
        _openRouterConfig = openRouterConfig;
        _logger = logger;
    }

    public async IAsyncEnumerable<string> AskStreamAsync(
        string question, int topK, IReadOnlyList<ChatTurn>? history = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("[RAG:OpenRouter] Starting pipeline ({Chars} chars)", question.Length);

        var t0 = sw.Elapsed;
        var questionEmbedding = await _embedder.GenerateEmbeddingAsync(question, ct);
        _logger.LogInformation("[RAG:OpenRouter] Embedding: {Ms}ms", (sw.Elapsed - t0).TotalMilliseconds.ToString("F0"));

        if (questionEmbedding is null)
        {
            yield return $"{ChatStreamTokens.ErrorSentinel}Could not generate embedding. Ensure Ollama is running with an embedding model.";
            yield break;
        }

        var candidateCount = topK * _ragConfig.RetrievalMultiplier;
        var t1 = sw.Elapsed;
        var candidates = await _vectorStore.SearchAsync(questionEmbedding, candidateCount, ct: ct);
        _logger.LogInformation("[RAG:OpenRouter] Vector search ({Candidates} candidates): {Ms}ms",
            candidateCount, (sw.Elapsed - t1).TotalMilliseconds.ToString("F0"));

        if (candidates.Count == 0)
        {
            yield return $"{ChatStreamTokens.ErrorSentinel}No relevant context found in the database. Try crawling some subreddits first.";
            yield break;
        }

        var reranked = candidates
            .OrderByDescending(c => OllamaChatService.CombinedScore(c))
            .Take(topK)
            .ToList();

        var ordered = OllamaChatService.ReorderForAttentionPublic(reranked);

        var context = OllamaChatService.BuildContextPublic(ordered);
        var prompt = OllamaChatService.BuildPromptPublic(question, context, history);

        // Read model at call time — hot-swappable from Settings UI
        var model = string.IsNullOrWhiteSpace(_openRouterConfig.Model)
            ? "deepseek/deepseek-chat"
            : _openRouterConfig.Model;

        _logger.LogInformation("[RAG:OpenRouter] Prompt: {Chars} chars, {Chunks} chunks, model: {Model}",
            prompt.Length, ordered.Count, model);

        var request = new ChatRequest
        {
            Model = model,
            Stream = true,
            Messages = [new ChatMessage { Role = "user", Content = prompt }]
        };

        // OWASP A02:2025 – API key injected at runtime from config, never hardcoded
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request),
        };
        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _openRouterConfig.ApiKey);
        httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/redditcrawler");

        var t2 = sw.Elapsed;
        using var httpResponse = await _http.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError("[RAG:OpenRouter] API returned {Status}: {Body}", (int)httpResponse.StatusCode, body);
            yield return $"{ChatStreamTokens.ErrorSentinel}OpenRouter returned HTTP {(int)httpResponse.StatusCode}. Check your API key and model.";
            yield break;
        }

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        var tokenCount = 0;
        var firstTokenLogged = false;

        // OpenAI SSE format: "data: {...}\n" lines, terminated by "data: [DONE]"
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null && !ct.IsCancellationRequested)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            if (json == "[DONE]")
            {
                var genMs = (sw.Elapsed - t2).TotalMilliseconds;
                _logger.LogInformation(
                    "[RAG:OpenRouter] Done: {Tokens} tokens in {Ms}ms ({TPS} tok/s) | total: {Total}ms",
                    tokenCount, genMs.ToString("F0"),
                    (tokenCount / (genMs / 1000.0)).ToString("F1"),
                    sw.ElapsedMilliseconds);
                break;
            }

            StreamChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<StreamChunk>(json); }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "[RAG:OpenRouter] Skipping malformed SSE chunk");
                continue;
            }

            var content = chunk?.Choices?[0]?.Delta?.Content;
            if (string.IsNullOrEmpty(content)) continue;

            if (!firstTokenLogged)
            {
                _logger.LogInformation("[RAG:OpenRouter] Time-to-first-token: {Ms}ms",
                    (sw.Elapsed - t2).TotalMilliseconds.ToString("F0"));
                firstTokenLogged = true;
            }

            tokenCount++;
            yield return content;
        }

        var sources = OllamaChatService.FormatSourcesPublic(ordered);
        yield return $"{ChatStreamTokens.SourcesSentinel}{sources}";
    }

    // ── OpenAI chat completions request/response models ──

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = [];
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class StreamChunk
    {
        [JsonPropertyName("choices")] public List<StreamChoice>? Choices { get; set; }
    }

    private sealed class StreamChoice
    {
        [JsonPropertyName("delta")] public DeltaContent? Delta { get; set; }
    }

    private sealed class DeltaContent
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
