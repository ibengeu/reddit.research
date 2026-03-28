using System.Collections.Concurrent;

namespace RedditCrawler.Web.Services;

/// <summary>
/// In-memory per-IP rate limiter. Allows up to <see cref="MaxRequests"/> chat
/// requests per IP within a sliding <see cref="WindowDuration"/>.
/// Stale entries are pruned on each check to avoid unbounded memory growth.
/// </summary>
public sealed class RateLimiterService
{
    public const int MaxRequests = 5;
    public static readonly TimeSpan WindowDuration = TimeSpan.FromHours(24);

    // IP → sorted list of request timestamps within the current window
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _store = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Returns true if the request is allowed; false if the IP has exceeded the limit.
    /// Records the attempt when allowed.
    /// </summary>
    public bool TryConsume(string ip)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - WindowDuration;

        lock (_lock)
        {
            var timestamps = _store.GetOrAdd(ip, _ => []);

            // Remove entries outside the window
            timestamps.RemoveAll(t => t < cutoff);

            if (timestamps.Count >= MaxRequests)
                return false;

            timestamps.Add(now);
            return true;
        }
    }

    /// <summary>
    /// Returns how many requests the IP has used and when the oldest one expires.
    /// </summary>
    public (int Used, DateTimeOffset? ResetsAt) GetStatus(string ip)
    {
        var cutoff = DateTimeOffset.UtcNow - WindowDuration;

        lock (_lock)
        {
            if (!_store.TryGetValue(ip, out var timestamps))
                return (0, null);

            var active = timestamps.Where(t => t >= cutoff).ToList();
            if (active.Count == 0) return (0, null);

            return (active.Count, active.Min() + WindowDuration);
        }
    }
}
