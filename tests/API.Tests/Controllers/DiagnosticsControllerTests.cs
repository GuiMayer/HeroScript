using API.Controllers;
using Core.Caching;
using Core.Logging;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace API.Tests.Controllers;

[Trait("Category", "Unit")]

/// <summary>
/// Mock implementation of Core.Logging.ILogger for testing.
/// </summary>
public class MockCoreLogger : ILogger
{
    public List<string> DebugMessages { get; } = new();
    public List<string> InformationMessages { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<string> ErrorMessages { get; } = new();

    public void LogDebug(string message) => DebugMessages.Add(message);
    public void LogInformation(string message) => InformationMessages.Add(message);
    public void LogWarning(string message) => WarningMessages.Add(message);
    public void LogError(string message) => ErrorMessages.Add(message);
    public void LogError(string message, Exception exception) => 
        ErrorMessages.Add($"{message}: {exception.Message}");
}

/// <summary>
/// Mock implementation of ICacheService for testing.
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

    public void Set(string key, object? value) => _cache[key] = value;

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
            _cache.Clear();
        else
            _cache.Remove(key);
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

public class DiagnosticsControllerTests
{
    private readonly CacheRegistry _registry;
    private readonly MockCoreLogger _logger;
    private readonly DiagnosticsController _controller;

    public DiagnosticsControllerTests()
    {
        _registry = CacheRegistry.Instance;
        _registry.Clear();
        _logger = new MockCoreLogger();
        _controller = new DiagnosticsController(_logger, _registry);
    }

    #region GetCacheStatistics Tests

    [Fact]
    public void GetCacheStatistics_WithNoCaches_ReturnsEmptyList()
    {
        var result = _controller.GetCacheStatistics() as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        
        var response = result.Value as CacheStatisticsResponse;
        Assert.NotNull(response);
        Assert.Empty(response.Services);
        Assert.Equal(0, response.TotalServices);
        Assert.Equal(0, response.AverageHitRate);
    }

    [Fact]
    public void GetCacheStatistics_WithSingleCache_ReturnsStatistics()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("key1", 42);
        _registry.Register(cache);

        var result = _controller.GetCacheStatistics() as OkObjectResult;

        Assert.NotNull(result);
        var response = result.Value as CacheStatisticsResponse;
        Assert.NotNull(response);
        Assert.Single(response.Services);
        Assert.Equal(1, response.TotalServices);
    }

    [Fact]
    public void GetCacheStatistics_WithMultipleCaches_ReturnsAllStatistics()
    {
        var cache1 = new MockCacheService("Cache1");
        var cache2 = new MockCacheService("Cache2");
        
        cache1.Set("a", 1);
        cache2.Set("b", 2);
        
        _registry.Register(cache1);
        _registry.Register(cache2);

        var result = _controller.GetCacheStatistics() as OkObjectResult;

        Assert.NotNull(result);
        var response = result.Value as CacheStatisticsResponse;
        Assert.NotNull(response);
        Assert.Equal(2, response.Services.Count);
        Assert.Equal(2, response.TotalServices);
    }

    [Fact]
    public void GetCacheStatistics_CalculatesAverageHitRate()
    {
        var cache1 = new MockCacheService("Cache1");
        var cache2 = new MockCacheService("Cache2");
        
        cache1.Set("a", 1);
        cache1.TryGetValue("a", out _);
        cache1.TryGetValue("a", out _);
        cache1.TryGetValue("missing", out _);
        
        cache2.Set("b", 2);
        cache2.TryGetValue("b", out _);
        cache2.TryGetValue("missing", out _);
        
        _registry.Register(cache1);
        _registry.Register(cache2);

        var result = _controller.GetCacheStatistics() as OkObjectResult;
        var response = result.Value as CacheStatisticsResponse;

        Assert.NotNull(response);
        Assert.True(response.AverageHitRate > 0.58 && response.AverageHitRate < 0.59);
    }

    #endregion

    #region GetCacheStatisticsByName Tests

    [Fact]
    public void GetCacheStatisticsByName_WithValidName_ReturnsStatistics()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("key1", 42);
        _registry.Register(cache);

        var result = _controller.GetCacheStatisticsByName("TestCache") as OkObjectResult;

        Assert.NotNull(result);
        var stats = result.Value as CacheServiceStats;
        Assert.NotNull(stats);
        Assert.Equal("TestCache", stats.CacheName);
        Assert.Equal(1, stats.Count);
    }

    [Fact]
    public void GetCacheStatisticsByName_WithEmptyName_ReturnsBadRequest()
    {
        var result = _controller.GetCacheStatisticsByName("") as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void GetCacheStatisticsByName_WithWhitespaceName_ReturnsBadRequest()
    {
        var result = _controller.GetCacheStatisticsByName("   ") as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void GetCacheStatisticsByName_WithNonExistentName_ReturnsNotFound()
    {
        var result = _controller.GetCacheStatisticsByName("nonexistent") as NotFoundObjectResult;

        Assert.NotNull(result);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region InvalidateCache Tests

    [Fact]
    public void InvalidateCache_WithValidName_InvalidatesCache()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        cache.Set("b", 2);
        _registry.Register(cache);

        var result = _controller.InvalidateCache("TestCache") as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        
        var stats = _registry.GetCacheStats("TestCache");
        Assert.Equal(0, stats!.Count);
    }

    [Fact]
    public void InvalidateCache_WithEmptyName_ReturnsBadRequest()
    {
        var result = _controller.InvalidateCache("") as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void InvalidateCache_WithNonExistentName_ReturnsNotFound()
    {
        var result = _controller.InvalidateCache("nonexistent") as NotFoundObjectResult;

        Assert.NotNull(result);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void InvalidateCache_ReturnsSuccessMessage()
    {
        var cache = new MockCacheService("TestCache");
        _registry.Register(cache);

        var result = _controller.InvalidateCache("TestCache") as OkObjectResult;
        var response = result!.Value as InvalidationResponse;

        Assert.NotNull(response);
        Assert.Contains("Successfully invalidated", response.Message);
    }

    #endregion

    #region InvalidateCacheKey Tests

    [Fact]
    public void InvalidateCacheKey_WithValidParameters_RemovesKey()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        cache.Set("b", 2);
        _registry.Register(cache);

        var result = _controller.InvalidateCacheKey("TestCache", "a") as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
        
        var stats = _registry.GetCacheStats("TestCache");
        Assert.Equal(1, stats!.Count);
    }

    [Fact]
    public void InvalidateCacheKey_WithEmptyCacheName_ReturnsBadRequest()
    {
        var result = _controller.InvalidateCacheKey("", "key") as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void InvalidateCacheKey_WithEmptyKey_ReturnsBadRequest()
    {
        var result = _controller.InvalidateCacheKey("cache", "") as BadRequestObjectResult;

        Assert.NotNull(result);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void InvalidateCacheKey_WithNonExistentCache_ReturnsNotFound()
    {
        var result = _controller.InvalidateCacheKey("nonexistent", "key") as NotFoundObjectResult;

        Assert.NotNull(result);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region GetCacheHealth Tests

    [Fact]
    public void GetCacheHealth_WithNoCaches_ReturnsUnknownStatus()
    {
        var result = _controller.GetCacheHealth() as OkObjectResult;
        var response = result!.Value as CacheHealthResponse;

        Assert.NotNull(response);
        Assert.Equal("Unknown", response.Status);
        Assert.Empty(response.Services);
    }

    [Fact]
    public void GetCacheHealth_HighHitRate_ReturnsHealthyStatus()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        
        for (int i = 0; i < 8; i++)
            cache.TryGetValue("a", out _);
        cache.TryGetValue("missing", out _);
        
        _registry.Register(cache);

        var result = _controller.GetCacheHealth() as OkObjectResult;
        var response = result!.Value as CacheHealthResponse;

        Assert.NotNull(response);
        Assert.Equal("Healthy", response.Status);
    }

    [Fact]
    public void GetCacheHealth_MediumHitRate_ReturnsFairStatus()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        
        cache.TryGetValue("a", out _);
        cache.TryGetValue("a", out _);
        cache.TryGetValue("a", out _);
        cache.TryGetValue("missing1", out _);
        cache.TryGetValue("missing2", out _);
        cache.TryGetValue("missing3", out _);
        
        _registry.Register(cache);

        var result = _controller.GetCacheHealth() as OkObjectResult;
        var response = result!.Value as CacheHealthResponse;

        Assert.NotNull(response);
        Assert.Equal("Fair", response.Status);
    }

    [Fact]
    public void GetCacheHealth_LowHitRate_ReturnsPoorStatus()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        
        cache.TryGetValue("a", out _);
        for (int i = 0; i < 10; i++)
            cache.TryGetValue($"missing{i}", out _);
        
        _registry.Register(cache);

        var result = _controller.GetCacheHealth() as OkObjectResult;
        var response = result!.Value as CacheHealthResponse;

        Assert.NotNull(response);
        Assert.Equal("Poor", response.Status);
    }

    [Fact]
    public void GetCacheHealth_IncludesServiceHealth()
    {
        var cache = new MockCacheService("TestCache");
        cache.Set("a", 1);
        cache.TryGetValue("a", out _);
        _registry.Register(cache);

        var result = _controller.GetCacheHealth() as OkObjectResult;
        var response = result!.Value as CacheHealthResponse;

        Assert.NotNull(response);
        Assert.Single(response.Services);
        
        var serviceHealth = response.Services[0];
        Assert.Equal("TestCache", serviceHealth.ServiceName);
        Assert.True(serviceHealth.IsHealthy);
    }

    #endregion
}
