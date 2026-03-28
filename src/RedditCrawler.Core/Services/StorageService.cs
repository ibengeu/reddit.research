using System.Text.Json;
using Microsoft.Extensions.Logging;
using RedditCrawler.Configuration;
using RedditCrawler.Models;

namespace RedditCrawler.Services;

public interface IStorageService : IAsyncDisposable
{
    Task WriteChunkAsync(LlmChunk chunk, CancellationToken ct = default);
    int ChunksWritten { get; }
}

/// <remarks>
/// Registered as Singleton in the Web UI. The StreamWriter is not disposed by DI on shutdown —
/// AutoFlush = true minimises data loss risk since every write is flushed immediately.
/// For proper disposal, register as Scoped or add explicit IHostedService cleanup.
/// </remarks>
public sealed class JsonlStorageService : IStorageService
{
    private readonly StreamWriter _writer;
    private readonly ILogger<JsonlStorageService> _logger;
    private int _chunksWritten;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public int ChunksWritten => _chunksWritten;

    public JsonlStorageService(CrawlerConfig config, ILogger<JsonlStorageService> logger)
    {
        _logger = logger;

        Directory.CreateDirectory(config.OutputPath);
        var subName = string.Join("+", config.Subreddits.Select(s => s.ToLowerInvariant()));
        if (subName.Length > 80) subName = subName[..80];
        var filePath = Path.Combine(config.OutputPath,
            $"{subName}_{config.Sort}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl");

        _writer = new StreamWriter(filePath, append: false, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };

        // OWASP A08:2025 – Log output path for audit trail, never log content/PII
        _logger.LogInformation("Writing output to {Path}", filePath);
    }

    public async Task WriteChunkAsync(LlmChunk chunk, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        await _writer.WriteLineAsync(json.AsMemory(), ct);
        Interlocked.Increment(ref _chunksWritten);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _logger.LogInformation("Storage closed. Total chunks written: {Count}", _chunksWritten);
    }
}
