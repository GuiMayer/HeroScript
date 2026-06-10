using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Caching;

/// <summary>
/// Generic LRU (Least Recently Used) cache with configurable size limits.
/// Thread-safe implementation with automatic eviction when capacity is reached.
/// </summary>
/// <typeparam name="TKey">Type of cache keys</typeparam>
/// <typeparam name="TValue">Type of cached values</typeparam>
public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maxCapacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
    private readonly LinkedList<CacheItem> _lruList;
    private readonly object _lock = new();

    // Statistics
    private long _hits;
    private long _misses;
    private long _evictions;
    private DateTime? _lastInvalidation;

    /// <summary>
    /// Creates a new LRU cache with the specified capacity.
    /// </summary>
    /// <param name="maxCapacity">Maximum number of items to store (default: 100)</param>
    public LruCache(int maxCapacity = 100)
    {
        if (maxCapacity <= 0)
            throw new ArgumentException("Capacity must be positive", nameof(maxCapacity));

        _maxCapacity = maxCapacity;
        _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(maxCapacity);
        _lruList = new LinkedList<CacheItem>();
    }

    /// <summary>
    /// Gets or sets a value in the cache.
    /// </summary>
    public TValue? this[TKey key]
    {
        get => TryGetValue(key, out var value) ? value : default;
        set
        {
            if (value != null)
                Set(key, value);
        }
    }

    /// <summary>
    /// Attempts to get a value from the cache.
    /// </summary>
    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                
                _hits++;
                value = node.Value.Value;
                return true;
            }

            _misses++;
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Adds or updates a value in the cache.
    /// </summary>
    public void Set(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                // Update existing item and move to front
                existingNode.Value.Value = value;
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
            }
            else
            {
                // Evict if at capacity
                if (_cache.Count >= _maxCapacity)
                {
                    EvictLeastRecentlyUsed();
                }

                // Add new item
                var cacheItem = new CacheItem { Key = key, Value = value };
                var node = _lruList.AddFirst(cacheItem);
                _cache[key] = node;
            }
        }
    }

    /// <summary>
    /// Removes a specific key from the cache.
    /// </summary>
    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _cache.Remove(key);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Removes all entries whose key matches the predicate.
    /// </summary>
    public int RemoveWhere(Func<TKey, bool> predicate)
    {
        lock (_lock)
        {
            var keysToRemove = _cache.Keys.Where(predicate).ToList();
            foreach (var key in keysToRemove)
            {
                if (_cache.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _cache.Remove(key);
                }
            }
            return keysToRemove.Count;
        }
    }

    /// <summary>
    /// Clears all items from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
            _lastInvalidation = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }

    /// <summary>
    /// Gets cache statistics for monitoring.
    /// </summary>
    public CacheStats GetStats()
    {
        lock (_lock)
        {
            long total = _hits + _misses;
            double hitRate = total > 0 ? (double)_hits / total : 0;

            return new CacheStats
            {
                Capacity = _maxCapacity,
                Count = _cache.Count,
                Hits = _hits,
                Misses = _misses,
                Evictions = _evictions,
                HitRate = hitRate,
                LastInvalidation = _lastInvalidation
            };
        }
    }

    /// <summary>
    /// Resets cache statistics.
    /// </summary>
    public void ResetStats()
    {
        lock (_lock)
        {
            _hits = 0;
            _misses = 0;
            _evictions = 0;
        }
    }

    /// <summary>
    /// Gets all keys currently in the cache (for diagnostics).
    /// </summary>
    public IEnumerable<TKey> GetKeys()
    {
        lock (_lock)
        {
            return _cache.Keys.ToList();
        }
    }

    private void EvictLeastRecentlyUsed()
    {
        // Remove last item (least recently used)
        var last = _lruList.Last;
        if (last != null)
        {
            _cache.Remove(last.Value.Key);
            _lruList.RemoveLast();
            _evictions++;
        }
    }

    private class CacheItem
    {
        public TKey Key { get; set; } = default!;
        public TValue Value { get; set; } = default!;
    }
}

/// <summary>
/// Statistics for cache performance monitoring.
/// </summary>
public class CacheStats
{
    public int Capacity { get; set; }
    public int Count { get; set; }
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long Evictions { get; set; }
    public double HitRate { get; set; }
    public DateTime? LastInvalidation { get; set; }
}
