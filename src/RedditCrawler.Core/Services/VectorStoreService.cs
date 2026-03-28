using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using RedditCrawler.Configuration;
using RedditCrawler.Models;

namespace RedditCrawler.Services;

public interface IVectorStoreService
{
    Task EnsureCollectionAsync(CancellationToken ct = default);
    Task UpsertChunkAsync(LlmChunk chunk, CancellationToken ct = default);
    Task<List<ScoredChunk>> SearchAsync(float[] queryEmbedding, int topK, string? subredditFilter = null, CancellationToken ct = default);
    Task<VectorStoreStats> GetStatsAsync(CancellationToken ct = default);
    bool IsEnabled { get; }
}

public sealed class NpgsqlVectorStoreService : IVectorStoreService, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly VectorConfig _config;
    private readonly ILogger<NpgsqlVectorStoreService> _logger;

    public bool IsEnabled => _config.Enabled;

    public NpgsqlVectorStoreService(VectorConfig config, ILogger<NpgsqlVectorStoreService> logger)
    {
        _config = config;
        _logger = logger;

        var builder = new NpgsqlDataSourceBuilder(config.ConnectionString);
        builder.UseVector();
        _dataSource = builder.Build();
    }

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            // OWASP A07:2025 – Table name is internal config, not user input; validated in VectorConfigValidator
            await using (var extCmd = conn.CreateCommand())
            {
                extCmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
                await extCmd.ExecuteNonQueryAsync(ct);
            }

            // Reload Npgsql type mappings so the 'vector' type is recognised in subsequent commands
            await conn.ReloadTypesAsync(ct);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"""
                    CREATE TABLE IF NOT EXISTS {_config.TableName} (
                        id UUID PRIMARY KEY,
                        content TEXT NOT NULL,
                        subreddit TEXT NOT NULL,
                        type TEXT NOT NULL,
                        author TEXT NOT NULL,
                        timestamp TEXT NOT NULL,
                        score INTEGER NOT NULL DEFAULT 0,
                        flair TEXT,
                        depth INTEGER NOT NULL DEFAULT 0,
                        permalink TEXT,
                        chunk_id TEXT NOT NULL,
                        embedding vector({_config.VectorSize}) NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS {_config.TableName}_embedding_idx
                        ON {_config.TableName} USING hnsw (embedding vector_cosine_ops);
                    CREATE INDEX IF NOT EXISTS {_config.TableName}_subreddit_idx
                        ON {_config.TableName} (subreddit);
                    """;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            _logger.LogInformation("Ensured pgvector table '{Table}' (dims: {Dims})",
                _config.TableName, _config.VectorSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure pgvector table");
            throw;
        }
    }

    public async Task UpsertChunkAsync(LlmChunk chunk, CancellationToken ct = default)
    {
        if (chunk.Embedding is null) return;

        var id = DeterministicGuid(chunk.Id);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_config.TableName}
                (id, content, subreddit, type, author, timestamp, score, flair, depth, permalink, chunk_id, embedding)
            VALUES
                (@id, @content, @subreddit, @type, @author, @timestamp, @score, @flair, @depth, @permalink, @chunk_id, @embedding)
            ON CONFLICT (id) DO UPDATE SET
                content    = EXCLUDED.content,
                subreddit  = EXCLUDED.subreddit,
                type       = EXCLUDED.type,
                author     = EXCLUDED.author,
                timestamp  = EXCLUDED.timestamp,
                score      = EXCLUDED.score,
                flair      = EXCLUDED.flair,
                depth      = EXCLUDED.depth,
                permalink  = EXCLUDED.permalink,
                chunk_id   = EXCLUDED.chunk_id,
                embedding  = EXCLUDED.embedding;
            """;

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("content", chunk.Content);
        cmd.Parameters.AddWithValue("subreddit", chunk.Subreddit);
        cmd.Parameters.AddWithValue("type", chunk.Type);
        cmd.Parameters.AddWithValue("author", chunk.Author);
        cmd.Parameters.AddWithValue("timestamp", chunk.Timestamp);
        cmd.Parameters.AddWithValue("score", chunk.Metadata.Score);
        cmd.Parameters.AddWithValue("flair", (object?)chunk.Metadata.Flair ?? DBNull.Value);
        cmd.Parameters.AddWithValue("depth", chunk.Metadata.Depth);
        cmd.Parameters.AddWithValue("permalink", (object?)chunk.Metadata.Permalink ?? DBNull.Value);
        cmd.Parameters.AddWithValue("chunk_id", chunk.Id);
        cmd.Parameters.AddWithValue("embedding", new Vector(chunk.Embedding));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<ScoredChunk>> SearchAsync(
        float[] queryEmbedding, int topK, string? subredditFilter = null, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        // pgvector cosine distance: <=> returns distance (0=identical, 2=opposite); similarity = 1 - distance
        var whereClause = string.IsNullOrWhiteSpace(subredditFilter)
            ? ""
            : "WHERE subreddit = @subreddit";

        cmd.CommandText = $"""
            SELECT content, subreddit, score, flair, timestamp,
                   1 - (embedding <=> @query) AS similarity
            FROM {_config.TableName}
            {whereClause}
            ORDER BY embedding <=> @query
            LIMIT @topK;
            """;

        cmd.Parameters.AddWithValue("query", new Vector(queryEmbedding));
        cmd.Parameters.AddWithValue("topK", topK);
        if (!string.IsNullOrWhiteSpace(subredditFilter))
            cmd.Parameters.AddWithValue("subreddit", subredditFilter);

        // OWASP A04:2025 – Defensive reader access; columns may theoretically be null on old rows
        var results = new List<ScoredChunk>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var content = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var sub = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var redditScore = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            var flair = reader.IsDBNull(3) ? null : reader.GetString(3);
            var timestamp = reader.IsDBNull(4) ? "" : reader.GetString(4);
            var similarity = reader.IsDBNull(5) ? 0f : (float)reader.GetDouble(5);

            results.Add(new ScoredChunk(
                Content: content,
                Subreddit: sub,
                SimilarityScore: similarity,
                RedditScore: redditScore,
                Flair: string.IsNullOrEmpty(flair) ? null : flair,
                Timestamp: timestamp
            ));
        }

        return results;
    }

    public async Task<VectorStoreStats> GetStatsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);

            await using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {_config.TableName};";
            var total = (long)(await countCmd.ExecuteScalarAsync(ct) ?? 0L);

            await using var statsCmd = conn.CreateCommand();
            statsCmd.CommandText = $"""
                SELECT subreddit, COUNT(*) AS cnt
                FROM {_config.TableName}
                GROUP BY subreddit
                ORDER BY cnt DESC;
                """;

            var subreddits = new List<SubredditStat>();
            await using var reader = await statsCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                var count = (ulong)reader.GetInt64(1);
                subreddits.Add(new SubredditStat(name, count));
            }

            return new VectorStoreStats((ulong)total, subreddits, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get vector store stats for table '{Table}'", _config.TableName);
            return new VectorStoreStats(0, [], false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
    }

    private static Guid DeterministicGuid(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}

public sealed class NoOpVectorStoreService : IVectorStoreService
{
    public bool IsEnabled => false;
    public Task EnsureCollectionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task UpsertChunkAsync(LlmChunk chunk, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<ScoredChunk>> SearchAsync(float[] queryEmbedding, int topK, string? subredditFilter = null, CancellationToken ct = default)
        => Task.FromResult<List<ScoredChunk>>([]);
    public Task<VectorStoreStats> GetStatsAsync(CancellationToken ct = default)
        => Task.FromResult(new VectorStoreStats(0, [], false));
}
