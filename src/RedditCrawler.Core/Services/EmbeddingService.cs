using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RedditCrawler.Configuration;

namespace RedditCrawler.Services;

public interface IEmbeddingService
{
    Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    bool IsEnabled { get; }
}

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _http;
    private readonly OllamaConfig _config;
    private readonly ILogger<OllamaEmbeddingService> _logger;

    public bool IsEnabled => _config.Enabled;

    public OllamaEmbeddingService(HttpClient http, OllamaConfig config, ILogger<OllamaEmbeddingService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (!_config.Enabled) return null;

        try
        {
            var request = new EmbedRequest { Model = _config.Model, Input = text };
            var response = await _http.PostAsJsonAsync("/api/embed", request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbedResponse>(ct);
            return result?.Embeddings?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Embedding generation failed: {Error}", ex.Message);
            return null;
        }
    }

    private sealed class EmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public string Input { get; set; } = "";
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}

public sealed class NoOpEmbeddingService : IEmbeddingService
{
    public bool IsEnabled => false;
    public Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken ct = default) =>
        Task.FromResult<float[]?>(null);
}
