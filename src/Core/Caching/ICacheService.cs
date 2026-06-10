using Core.Common;

namespace Core.Caching;

/// <summary>
/// Abstraction for cache monitoring and management.
/// Allows centralized monitoring of cache statistics across different cache implementations.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Name/identifier of this cache for monitoring.
    /// </summary>
    string CacheName { get; }

    /// <summary>
    /// Invalidates cache entries. Behavior depends on implementation.
    /// </summary>
    /// <param name="key">Optional key to invalidate. If null, invalidates all.</param>
    void Invalidate(string? key = null);

    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    CacheServiceStats GetStats();
}

/// <summary>
/// Unified cache statistics for monitoring and diagnostics.
/// Aligns with LruCache.CacheStats and API requirements.
/// </summary>
public class CacheServiceStats
{
    public string CacheName { get; set; } = "";
    public int Capacity { get; set; }
    public int Count { get; set; }
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long Evictions { get; set; }
    public double HitRate { get; set; }
    public DateTime? LastInvalidation { get; set; }
}
