using System.Collections.Concurrent;

namespace YoutubeResearchMcp.Services;

/// <summary>
/// Singleton store that holds one ResearchSession per browser session ID.
/// Entries are evicted after 4 hours of inactivity to prevent unbounded memory growth.
/// </summary>
public sealed class ResearchSessionStore : IDisposable
{
    private static readonly TimeSpan IdleTimeout   = TimeSpan.FromHours(4);
    private static readonly TimeSpan EvictInterval = TimeSpan.FromMinutes(30);

    private sealed class Entry
    {
        public ResearchSession Session     { get; } = new();
        public DateTime        LastAccessed { get; set; } = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Entry> _sessions = new();
    private readonly Timer _evictionTimer;

    public ResearchSessionStore()
    {
        _evictionTimer = new Timer(Evict, null, EvictInterval, EvictInterval);
    }

    public ResearchSession GetOrCreate(string sessionId)
    {
        var entry = _sessions.GetOrAdd(sessionId, _ => new Entry());
        entry.LastAccessed = DateTime.UtcNow;
        return entry.Session;
    }

    private void Evict(object? _)
    {
        var cutoff = DateTime.UtcNow - IdleTimeout;
        foreach (var (key, entry) in _sessions)
        {
            if (entry.LastAccessed < cutoff)
                _sessions.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }
    }

    public void Dispose() => _evictionTimer.Dispose();
}
