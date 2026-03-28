using System.Text.Json;
using RedditCrawler.Configuration;

namespace RedditCrawler.Web.Services;

/// <summary>
/// Persists the four runtime-mutable config objects to appsettings.user.json.
/// ScheduleSave() debounces writes — rapid UI changes coalesce into one disk write
/// after a short idle delay. The file is written atomically via temp-file rename.
/// </summary>
public sealed class SettingsPersistenceService : IAsyncDisposable
{
    private readonly string _persistDir;
    private readonly ILogger<SettingsPersistenceService> _logger;
    private readonly OllamaConfig _ollama;
    private readonly VectorConfig _vector;
    private readonly RagConfig _rag;
    private readonly OpenRouterConfig _openRouter;

    private CancellationTokenSource? _debounce;
    private readonly object _debounceLock = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public SettingsPersistenceService(
        string persistDir,
        ILogger<SettingsPersistenceService> logger,
        OllamaConfig ollama,
        VectorConfig vector,
        RagConfig rag,
        OpenRouterConfig openRouter)
    {
        _persistDir = persistDir;
        _logger = logger;
        _ollama = ollama;
        _vector = vector;
        _rag = rag;
        _openRouter = openRouter;
    }

    /// <summary>
    /// Schedule a debounced save. Repeated calls within the delay window reset the timer.
    /// </summary>
    public void ScheduleSave(TimeSpan delay = default)
    {
        if (delay == default) delay = TimeSpan.FromMilliseconds(800);

        CancellationToken token;
        lock (_debounceLock)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            token = _debounce.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token);
                await FlushAsync(token);
            }
            catch (OperationCanceledException) { /* superseded by a newer schedule */ }
        });
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var payload = new
            {
                Ollama = new
                {
                    _ollama.Enabled,
                    _ollama.BaseUrl,
                    _ollama.Model
                },
                Vector = new
                {
                    _vector.Enabled,
                    _vector.ConnectionString,
                    _vector.TableName,
                    _vector.VectorSize
                },
                Rag = new
                {
                    _rag.TopK,
                    _rag.ChatModel,
                    _rag.RetrievalMultiplier,
                    ChatProvider = _rag.ChatProvider.ToString()
                },
                OpenRouter = new
                {
                    _openRouter.Enabled,
                    _openRouter.BaseUrl,
                    _openRouter.Model,
                    _openRouter.ApiKey
                }
            };

            var path = Path.Combine(_persistDir, "appsettings.user.json");
            var tmp = path + ".tmp";

            try
            {
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                await File.WriteAllTextAsync(tmp, json, ct);
                File.Move(tmp, path, overwrite: true);
                _logger.LogDebug("Settings autosaved to {Path}", path);
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Settings autosave failed");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
