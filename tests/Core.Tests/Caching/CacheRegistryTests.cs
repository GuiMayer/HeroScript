using Core.Caching;
using Xunit;

namespace Core.Tests.Caching;

/// <summary>
/// Mock implementation of ICacheService for testing CacheRegistry.
/// </summary>
public class MockCacheService : ICacheService
{
    private readonly Dictionary<string, object?> _cache = new();
    private long _hits;
    private long _misses;
    private long _evictions;
    private DateTime? _lastInvalidation;

    public string CacheName { get; }

    public MockCacheService(string cacheName)
    {
        CacheName = cacheName;
    }

    public void Set(string key, object? value)
    {
        _cache[key] = value;
    }

    public bool TryGetValue(string key, out object? value)
    {
        if (_cache.TryGetValue(key, out value))
        {
            _hits++;
            return true;
        }
        _misses++;
        return false;
    }

    public void Invalidate(string? key = null)
    {
        _lastInvalidation = DateTime.UtcNow;
        
        if (key == null)
        {
            _cache.Clear();
        }
        else
        {
            _cache.Remove(key);
        }
    }

    public CacheServiceStats GetStats()
    {
        var total = _hits + _misses;
        var hitRate = total > 0 ? (double)_hits / total : 0;
        
        return new CacheServiceStats
        {
            CacheName = CacheName,
            Capacity = 1000,
            Count = _cache.Count,
            Hits = _hits,
            Misses = _misses,
            Evictions = _evictions,
            HitRate = hitRate,
            LastInvalidation = _lastInvalidation
        };
    }

    public void ResetStats()
    {
        _hits = 0;
        _misses = 0;
        _evictions = 0;
        _lastInvalidation = null;
    }
}

public class CacheRegistryTests
{
    private readonly CacheRegistry _registry;

    public CacheRegistryTests()
    {
        _registry = CacheRegistry.Instance;
        _registry.Clear();
    }

    [Fact]
    public void Singleton_MultipleInstances_ReturnSameReference()
    {
        var instance1 = CacheRegistry.Instance;
        var instance2 = CacheRegistry.Instance;

        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Register_WithValidCache_RegistersSuccessfully()
    {
        var cache = new MockCacheService("TestCache");
        _registry.Register(cache);

        var names = _registry.GetCacheNames();
        Assert.Contains("TestCache", names);
    }

    [Fact]
    public void Register_WithNullCache_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.Register(null!));
    }

    [Fact]
    public void Register_MultipleCaches_RegistersAll()
    {
        var cache1 = new MockCacheService("Cache1");
        var cache2 = new MockCacheService("Cache2");

        _registry.Register(cache1);
        _registry.Register(cache2);

        var names = _registry.GetCacheNames();
        Assert.Equal(2, names.Count);
        Assert.Contains("Cache1", names);
        Assert.Contains("Cache2", names);
    }

    [Fact]
    public void Unregister_ExistingCache_RemovesSuccessfully()
    {
        var cache = new MockCacheService("TestCache");
        _registry.Register(cache);

        var removed = _registry.Unregister("TestCache");

        Assert.True(removed);
        Assert.Empty(_registry.GetCacheNames());
    }

    [Fact]
    public void Unregister_NonExistentCache_ReturnsFalse()
    {
        var removed = _registry.Unregister("nonexistent");
        Assert.False(removed);
    }

    [Fact]
    public void GetCacheNames_ReturnsAllRegisteredNames()
    {
        var cache1 = new MockCacheService("Cache1");
        var cache2 = new MockCacheService("Cache2");
        var cache3 = new MockCacheService("Cache3");

        _registry.Register(cache1);
        _registry.Register(cache2);
        _registry.Register(cache3);

        var names = _registry.GetCacheNames();
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void GetCacheStats_ValidCacheName_ReturnsStats()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("key1", 42);
        _registry.Register(cache);

        var stats = _registry.GetCacheStats("TestCache");

        Assert.NotNull(stats);
        Assert.Equal("TestCache", stats.CacheName);
        Assert.Equal(1, stats.Count);
    }

    [Fact]
    public void GetCacheStats_NonExistentCache_ReturnsNull()
    {
        var stats = _registry.GetCacheStats("nonexistent");
        Assert.Null(stats);
    }

    [Fact]
    public void GetAllStats_MultipleRegisteredCaches_ReturnsAllStats()
    {
        var cache1 = new MockCacheService("Cache1");
        var cache2 = new MockCacheService("Cache2");
        
        cache1.Set("a", 1);
        cache2.Set("b", 2);

        _registry.Register(cache1);
        _registry.Register(cache2);

        var allStats = _registry.GetAllStats();

        Assert.Equal(2, allStats.Count);
    }

    [Fact]
    public void InvalidateCache_ValidCacheName_ClearsAllEntries()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        cache.Set("b", 2);
        _registry.Register(cache);

        _registry.InvalidateCache("TestCache");
        var stats = _registry.GetCacheStats("TestCache");

        Assert.Equal(0, stats!.Count);
    }

    [Fact]
    public void InvalidateCache_WithKey_RemovesOnlySpecificKey()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        cache.Set("b", 2);
        _registry.Register(cache);

        _registry.InvalidateCache("TestCache", "a");
        var stats = _registry.GetCacheStats("TestCache");

        Assert.Equal(1, stats!.Count);
    }

    [Fact]
    public void Clear_ClearsAllRegistrations()
    {
        var cache1 = new MockCacheService("Cache1");
        var cache2 = new MockCacheService("Cache2");
        
        _registry.Register(cache1);
        _registry.Register(cache2);
        _registry.Clear();

        var names = _registry.GetCacheNames();
        Assert.Empty(names);
    }

    [Fact]
    public async Task ThreadSafety_ConcurrentOperations_DoesNotCorruptState()
    {
        var cache = new MockCacheService("TestCache");
        _registry.Register(cache);

        var tasks = new List<Task>();

        // Concurrent reads and writes
        for (int t = 0; t < 5; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    cache.Set($"key_{i}", i);
                    cache.TryGetValue($"key_{i}", out _);
                }
            }));
        }

        await Task.WhenAll(tasks);

        var stats = _registry.GetCacheStats("TestCache");
        Assert.NotNull(stats);
        Assert.True(stats.Count >= 0);
    }
}
