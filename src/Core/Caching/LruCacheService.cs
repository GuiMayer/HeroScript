namespace Core.Caching;

/// <summary>
/// Adapter wrapping LruCache to implement ICacheService.
/// Allows LruCache to be registered in the CacheRegistry for monitoring.
/// </summary>
public sealed class LruCacheService<TKey, TValue> : ICacheService where TKey : notnull
{
    private readonly LruCache<TKey, TValue> _cache;
    private readonly string _cacheName;

    public string CacheName => _cacheName;

    public LruCacheService(LruCache<TKey, TValue> cache, string cacheName)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cacheName = string.IsNullOrWhiteSpace(cacheName)
            ? throw new ArgumentException("Cache name cannot be empty", nameof(cacheName))
            : cacheName;
    }

    public void Invalidate(string? key = null)
    {
        // LruCache doesn't support selective key invalidation by string key
        // So null = clear all, non-null = clear all (no-op for consistency)
        if (string.IsNullOrWhiteSpace(key))
        {
            _cache.Clear();
        }
    }

    public CacheServiceStats GetStats()
    {
        var stats = _cache.GetStats();
        return new CacheServiceStats
        {
            CacheName = _cacheName,
            Capacity = stats.Capacity,
            Count = stats.Count,
            Hits = stats.Hits,
            Misses = stats.Misses,
            Evictions = stats.Evictions,
            HitRate = stats.HitRate,
            LastInvalidation = stats.LastInvalidation
        };
    }
}
