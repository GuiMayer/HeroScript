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
    /// Dependency layer used to order broad invalidations from source data to
    /// increasingly derived views.
    /// </summary>
    CacheLayer Layer => CacheLayer.Definition;

    /// <summary>
    /// Revision-addressed caches are immutable historical data. They are kept
    /// during ordinary authoring reloads and may only be cleared explicitly.
    /// </summary>
    bool PreserveAcrossGlobalInvalidation => false;

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

public enum CacheLayer
{
    Source = 0,
    Manifest = 100,
    Definition = 200,
    Runtime = 300,
    Derived = 400,
    Revisioned = 500
}

public sealed record CacheInvalidationEntry(
    string CacheName,
    CacheLayer Layer,
    bool Invalidated,
    string? Reason = null);

public sealed record CacheInvalidationReport(
    IReadOnlyList<CacheInvalidationEntry> Entries)
{
    public int InvalidatedCount => Entries.Count(entry => entry.Invalidated);
    public int PreservedCount => Entries.Count - InvalidatedCount;
}

public interface ICacheCoordinator
{
    void Register(ICacheService cache);
    bool Unregister(string cacheName);
    IReadOnlyList<string> GetCacheNames();
    CacheServiceStats? GetCacheStats(string cacheName);
    IReadOnlyList<CacheServiceStats> GetAllStats();
    bool InvalidateCache(string cacheName, string? key = null);
    CacheInvalidationReport InvalidateAll(string? key = null, bool includeRevisioned = false);
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
