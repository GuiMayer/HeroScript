using Core.Caching;
using Xunit;

namespace Core.Tests.Caching;

public class LruCacheTests
{
    [Fact]
    public void Constructor_WithValidCapacity_CreatesEmptyCache()
    {
        var cache = new LruCache<string, int>(50);

        Assert.Equal(0, cache.Count);
        var stats = cache.GetStats();
        Assert.Equal(50, stats.Capacity);
        Assert.Equal(0, stats.Count);
    }

    [Fact]
    public void Constructor_WithZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LruCache<string, int>(0));
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_Throws()
    {
        Assert.Throws<ArgumentException>(() => new LruCache<string, int>(-1));
    }

    [Fact]
    public void Set_And_TryGetValue_ReturnsStoredValue()
    {
        var cache = new LruCache<string, string>(10);
        cache.Set("key1", "value1");

        var found = cache.TryGetValue("key1", out var value);

        Assert.True(found);
        Assert.Equal("value1", value);
    }

    [Fact]
    public void TryGetValue_MissingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(10);

        var found = cache.TryGetValue("missing", out var value);

        Assert.False(found);
        Assert.Equal(default, value);
    }

    [Fact]
    public void Set_UpdatesExistingKey()
    {
        var cache = new LruCache<string, string>(10);
        cache.Set("key1", "v1");
        cache.Set("key1", "v2");

        cache.TryGetValue("key1", out var value);

        Assert.Equal("v2", value);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Indexer_Get_ReturnsValue()
    {
        var cache = new LruCache<string, string>(10);
        cache.Set("key1", "value1");

        Assert.Equal("value1", cache["key1"]);
    }

    [Fact]
    public void Indexer_Get_MissingKey_ReturnsDefault()
    {
        var cache = new LruCache<string, string>(10);

        Assert.Null(cache["missing"]);
    }

    [Fact]
    public void Indexer_Set_StoresValue()
    {
        var cache = new LruCache<string, string>(10);
        cache["key1"] = "value1";

        Assert.Equal("value1", cache["key1"]);
    }

    [Fact]
    public void Remove_ExistingKey_ReturnsTrue()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("key1", 42);

        var removed = cache.Remove("key1");

        Assert.True(removed);
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGetValue("key1", out _));
    }

    [Fact]
    public void Remove_MissingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(10);

        Assert.False(cache.Remove("missing"));
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGetValue("a", out _));
    }

    [Fact]
    public void Clear_SetsLastInvalidation()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);

        var before = DateTime.UtcNow;
        cache.Clear();
        var after = DateTime.UtcNow;

        var stats = cache.GetStats();
        Assert.NotNull(stats.LastInvalidation);
        Assert.InRange(stats.LastInvalidation.Value, before, after);
    }

    // --- Eviction tests ---

    [Fact]
    public void Eviction_WhenCapacityReached_RemovesLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        // "a" is LRU, should be evicted
        cache.Set("d", 4);

        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGetValue("a", out _));
        Assert.True(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("c", out _));
        Assert.True(cache.TryGetValue("d", out _));
    }

    [Fact]
    public void Eviction_AccessPromotesItem()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        // Access "a" to promote it — now "b" is LRU
        cache.TryGetValue("a", out _);
        cache.Set("d", 4);

        Assert.True(cache.TryGetValue("a", out _));
        Assert.False(cache.TryGetValue("b", out _)); // "b" evicted
        Assert.True(cache.TryGetValue("c", out _));
        Assert.True(cache.TryGetValue("d", out _));
    }

    [Fact]
    public void Eviction_UpdatePromotesItem()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        // Update "a" promotes it — now "b" is LRU
        cache.Set("a", 10);
        cache.Set("d", 4);

        Assert.True(cache.TryGetValue("a", out var aVal));
        Assert.Equal(10, aVal);
        Assert.False(cache.TryGetValue("b", out _)); // "b" evicted
    }

    // --- Stats tests ---

    [Fact]
    public void Stats_TracksHitsAndMisses()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);

        cache.TryGetValue("a", out _);  // hit
        cache.TryGetValue("a", out _);  // hit
        cache.TryGetValue("b", out _);  // miss

        var stats = cache.GetStats();
        Assert.Equal(2, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(2.0 / 3.0, stats.HitRate, 4);
    }

    [Fact]
    public void Stats_TracksEvictions()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3); // evicts "a"
        cache.Set("d", 4); // evicts "b"

        var stats = cache.GetStats();
        Assert.Equal(2, stats.Evictions);
    }

    [Fact]
    public void ResetStats_ClearsCounters()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.TryGetValue("a", out _);
        cache.TryGetValue("missing", out _);

        cache.ResetStats();

        var stats = cache.GetStats();
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
        Assert.Equal(0, stats.Evictions);
    }

    [Fact]
    public void Stats_EmptyCache_HitRateIsZero()
    {
        var cache = new LruCache<string, int>(10);
        var stats = cache.GetStats();

        Assert.Equal(0, stats.HitRate);
    }

    // --- GetKeys tests ---

    [Fact]
    public void GetKeys_ReturnsAllCachedKeys()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        var keys = cache.GetKeys().OrderBy(k => k).ToList();

        Assert.Equal(new[] { "a", "b", "c" }, keys);
    }

    [Fact]
    public void GetKeys_ReturnsSnapshotNotLiveReference()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);

        var keys = cache.GetKeys();
        cache.Set("b", 2);

        Assert.Single(keys);
    }

    // --- RemoveWhere tests ---

    [Fact]
    public void RemoveWhere_RemovesMatchingKeys()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("default::sword", 1);
        cache.Set("default::shield", 2);
        cache.Set("modded::sword", 3);

        var removed = cache.RemoveWhere(k => k.EndsWith("::sword"));

        Assert.Equal(2, removed);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGetValue("default::shield", out _));
    }

    [Fact]
    public void RemoveWhere_NoMatches_ReturnsZero()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);

        var removed = cache.RemoveWhere(k => k == "nonexistent");

        Assert.Equal(0, removed);
        Assert.Equal(1, cache.Count);
    }

    // --- Thread safety test ---

    [Fact]
    public async Task ConcurrentAccess_DoesNotCorruptState()
    {
        var cache = new LruCache<int, int>(50);
        var tasks = new List<Task>();

        for (int t = 0; t < 10; t++)
        {
            var offset = t * 100;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    cache.Set(offset + i, i);
                    cache.TryGetValue(offset + i, out _);
                    if (i % 3 == 0) cache.Remove(offset + i);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Should not throw, count should be consistent
        Assert.True(cache.Count >= 0 && cache.Count <= 50);
        var stats = cache.GetStats();
        Assert.True(stats.Hits >= 0);
        Assert.True(stats.Misses >= 0);
    }

    // --- Capacity = 1 edge case ---

    [Fact]
    public void Capacity1_EvictsOnEveryNewKey()
    {
        var cache = new LruCache<string, int>(1);
        cache.Set("a", 1);
        cache.Set("b", 2);

        Assert.Equal(1, cache.Count);
        Assert.False(cache.TryGetValue("a", out _));
        Assert.True(cache.TryGetValue("b", out _));
    }
}
