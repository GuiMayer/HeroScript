namespace API.Models;

/// <summary>
/// Cache statistics for resource loading
/// </summary>
public class CacheStatsDto
{
    /// <summary>
    /// Number of cached resources
    /// </summary>
    public int CachedResources { get; set; }

    /// <summary>
    /// Total cache hits
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// Total cache misses
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// Last cache invalidation timestamp
    /// </summary>
    public DateTime? LastInvalidation { get; set; }
}
