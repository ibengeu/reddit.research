using Npgsql;
using RedditCrawler.Configuration;

namespace RedditCrawler.Web.Services;

public sealed class ConnectionHealthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaConfig _ollamaConfig;
    private readonly VectorConfig _vectorConfig;
    private readonly ILogger<ConnectionHealthService> _logger;

    // volatile ensures concurrent reads from the UI thread see the latest written value
    private volatile string _ollamaStatus = "unknown";
    private volatile string _ollamaStatusText = "Not checked";
    private volatile string _vectorStatus = "unknown";
    private volatile string _vectorStatusText = "Not checked";

    public string OllamaStatus => _ollamaStatus;
    public string OllamaStatusText => _ollamaStatusText;
    public string VectorStatus => _vectorStatus;
    public string VectorStatusText => _vectorStatusText;

    public event Action? OnHealthChanged;

    public ConnectionHealthService(
        IHttpClientFactory httpClientFactory,
        OllamaConfig ollamaConfig,
        VectorConfig vectorConfig,
        ILogger<ConnectionHealthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _ollamaConfig = ollamaConfig;
        _vectorConfig = vectorConfig;
        _logger = logger;
    }

    public async Task CheckAllAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(CheckOllamaAsync(ct), CheckVectorAsync(ct));
        OnHealthChanged?.Invoke();
    }

    public async Task CheckOllamaAsync(CancellationToken ct = default)
    {
        try
        {
            var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var resp = await http.GetAsync($"{_ollamaConfig.BaseUrl}/api/tags", ct);
            if (resp.IsSuccessStatusCode)
            {
                _ollamaStatus = "ok";
                _ollamaStatusText = "Connected";
            }
            else
            {
                _ollamaStatus = "error";
                _ollamaStatusText = $"HTTP {(int)resp.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            _ollamaStatus = "error";
            // OWASP A08:2025 – Never expose internal details (IPs, paths) to UI
            _ollamaStatusText = ex is HttpRequestException ? "Connection refused" : "Unreachable";
            _logger.LogDebug(ex, "Ollama health check failed");
        }
    }

    public async Task CheckVectorAsync(CancellationToken ct = default)
    {
        if (!_vectorConfig.Enabled)
        {
            _vectorStatus = "unknown";
            _vectorStatusText = "Disabled";
            return;
        }

        try
        {
            await using var conn = new NpgsqlConnection(_vectorConfig.ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            await cmd.ExecuteScalarAsync(ct);

            _vectorStatus = "ok";
            _vectorStatusText = "Connected";
        }
        catch (Exception ex)
        {
            _vectorStatus = "error";
            // OWASP A08:2025 – Never expose internal details (connection strings, paths) to UI
            _vectorStatusText = ex is NpgsqlException ? "Connection refused" : "Unreachable";
            _logger.LogDebug(ex, "PostgreSQL health check failed");
        }
    }
}
