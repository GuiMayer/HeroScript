namespace API.Models;

/// <summary>
/// Unified cache statistics for monitoring and diagnostics.
/// Aggregates statistics from various cache implementations across Core.
/// </summary>
public class CacheStatsDto
{
    /// <summary>
    /// Name/identifier of the cache.
    /// </summary>
    public string CacheName { get; set; } = "";

    /// <summary>
    /// Maximum capacity of the cache.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Current number of items in cache.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Total successful cache lookups.
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Total failed cache lookups.
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Total items evicted due to capacity limits.
    /// </summary>
    public long Evictions { get; set; }

    /// <summary>
    /// Cache hit rate as percentage (0.0 to 1.0).
    /// </summary>
    public double HitRate { get; set; }

    /// <summary>
    /// Last cache invalidation/clear timestamp.
    /// </summary>
    public DateTime? LastInvalidation { get; set; }
}
