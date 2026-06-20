using Core.Caching;
using Xunit;

namespace Core.Tests.Caching;

public class LruCacheServiceTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesService()
    {
        var cache = new LruCache<string, int>(10);
        var service = new LruCacheService<string, int>(cache, "TestCache");

        Assert.NotNull(service);
        Assert.Equal("TestCache", service.CacheName);
    }

    [Fact]
    public void CacheName_ReturnsProvidedName()
    {
        var cache = new LruCache<string, int>(10);
        var service = new LruCacheService<string, int>(cache, "MyCache");

        Assert.Equal("MyCache", service.CacheName);
    }

    [Fact]
    public void GetStats_ReturnsStatsWithCacheName()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("key1", 42);
        var service = new LruCacheService<string, int>(cache, "TestCache");

        var stats = service.GetStats();

        Assert.NotNull(stats);
        Assert.Equal("TestCache", stats.CacheName);
        Assert.Equal(1, stats.Count);
        Assert.Equal(10, stats.Capacity);
    }

    [Fact]
    public void Invalidate_WithoutKey_ClearsAllEntries()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("b", 2);
        var service = new LruCacheService<string, int>(cache, "TestCache");

        service.Invalidate();

        var stats = service.GetStats();
        Assert.Equal(0, stats.Count);
    }

    [Fact]
    public void Invalidate_WithNullKey_ClearsAllEntries()
    {
        var cache = new LruCache<string, int>(10);
        cache.Set("a", 1);
        cache.Set("b", 2);
        var service = new LruCacheService<string, int>(cache, "TestCache");

        service.Invalidate(null);

        var stats = service.GetStats();
        Assert.Equal(0, stats.Count);
    }
}
