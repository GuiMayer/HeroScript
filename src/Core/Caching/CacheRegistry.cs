namespace Core.Caching;

/// <summary>
/// Centralized registry for cache monitoring.
/// Allows aggregating statistics from multiple cache implementations.
/// </summary>
public sealed class CacheRegistry : ICacheCoordinator
{
    private static readonly Lazy<CacheRegistry> _instance = new(() => new CacheRegistry());
    private readonly Dictionary<string, ICacheService> _caches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public static CacheRegistry Instance => _instance.Value;

    public CacheRegistry()
    {
    }

    /// <summary>
    /// Registers a cache service for monitoring.
    /// </summary>
    public void Register(ICacheService cache)
    {
        if (cache == null)
            throw new ArgumentNullException(nameof(cache));

        lock (_lock)
        {
            _caches[cache.CacheName] = cache;
        }
    }

    /// <summary>
    /// Unregisters a cache service.
    /// </summary>
    public bool Unregister(string cacheName)
    {
        lock (_lock)
        {
            return _caches.Remove(cacheName);
        }
    }

    /// <summary>
    /// Gets all registered cache names.
    /// </summary>
    public IReadOnlyList<string> GetCacheNames()
    {
        lock (_lock)
        {
            return _caches.Values
                .OrderBy(cache => cache.Layer)
                .ThenBy(cache => cache.CacheName, StringComparer.Ordinal)
                .Select(cache => cache.CacheName)
                .ToList();
        }
    }

    /// <summary>
    /// Gets statistics for a specific cache.
    /// </summary>
    public CacheServiceStats? GetCacheStats(string cacheName)
    {
        lock (_lock)
        {
            return _caches.TryGetValue(cacheName, out var cache)
                ? cache.GetStats()
                : null;
        }
    }

    /// <summary>
    /// Gets statistics for all registered caches.
    /// </summary>
    public IReadOnlyList<CacheServiceStats> GetAllStats()
    {
        lock (_lock)
        {
            return _caches.Values
                .OrderBy(cache => cache.Layer)
                .ThenBy(cache => cache.CacheName, StringComparer.Ordinal)
                .Select(c => c.GetStats())
                .ToList();
        }
    }

    /// <summary>
    /// Invalidates a specific cache.
    /// </summary>
    public bool InvalidateCache(string cacheName, string? key = null)
    {
        ICacheService? cache;
        lock (_lock)
        {
            _caches.TryGetValue(cacheName, out cache);
        }

        if (cache == null)
            return false;

        cache.Invalidate(key);
        return true;
    }

    public CacheInvalidationReport InvalidateAll(string? key = null, bool includeRevisioned = false)
    {
        ICacheService[] ordered;
        lock (_lock)
        {
            ordered = _caches.Values
                .OrderBy(cache => cache.Layer)
                .ThenBy(cache => cache.CacheName, StringComparer.Ordinal)
                .ToArray();
        }

        var entries = new List<CacheInvalidationEntry>(ordered.Length);
        foreach (var cache in ordered)
        {
            if (cache.PreserveAcrossGlobalInvalidation && !includeRevisioned)
            {
                entries.Add(new CacheInvalidationEntry(
                    cache.CacheName,
                    cache.Layer,
                    false,
                    "revision-addressed cache preserved"));
                continue;
            }

            cache.Invalidate(key);
            entries.Add(new CacheInvalidationEntry(cache.CacheName, cache.Layer, true));
        }

        return new CacheInvalidationReport(entries);
    }

    /// <summary>
    /// Clears all registrations (for testing).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _caches.Clear();
        }
    }
}
