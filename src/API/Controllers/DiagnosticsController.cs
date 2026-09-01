using Microsoft.AspNetCore.Mvc;
using Core.Caching;
using API.Models;
using CoreLogger = Core.Logging.ILogger;

namespace API.Controllers;

/// <summary>
/// Diagnostics and monitoring endpoints for system health and performance metrics.
/// Provides cache statistics, performance monitoring, and system information.
/// </summary>
[ApiController]
[Route("api/v1/admin/diagnostics")]
public class DiagnosticsController : ControllerBase
{
    private readonly CoreLogger _logger;
    private readonly ICacheCoordinator _cacheRegistry;

    public DiagnosticsController(CoreLogger logger, ICacheCoordinator cacheRegistry)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheRegistry = cacheRegistry ?? throw new ArgumentNullException(nameof(cacheRegistry));
    }

    /// <summary>
    /// Gets comprehensive cache statistics for all registered cache services.
    /// </summary>
    [HttpGet("cache/stats")]
    [ProducesResponseType(typeof(CacheStatisticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult GetCacheStatistics()
    {
        try
        {
            var allStats = _cacheRegistry.GetAllStats();
            
            var response = new CacheStatisticsResponse
            {
                Timestamp = DateTime.UtcNow,
                Services = allStats.ToList(),
                TotalServices = allStats.Count,
                AverageHitRate = CalculateAverageHitRate(allStats)
            };
            
            _logger.LogInformation("Retrieved cache statistics");
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting cache statistics: {ex.Message}");
            return StatusCode(500, new ErrorResponse { Error = $"Failed to get cache statistics: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets statistics for a specific cache service by name.
    /// </summary>
    [HttpGet("cache/stats/{cacheName}")]
    [ProducesResponseType(typeof(CacheServiceStats), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult GetCacheStatisticsByName(string cacheName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cacheName))
            {
                return BadRequest(new ErrorResponse { Error = "Cache name cannot be empty" });
            }

            var stats = _cacheRegistry.GetCacheStats(cacheName);
            if (stats == null)
            {
                return NotFound(new ErrorResponse { Error = $"Cache service not found: {cacheName}" });
            }

            _logger.LogInformation("Retrieved statistics for cache");
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting cache statistics: {ex.Message}");
            return StatusCode(500, new ErrorResponse { Error = $"Failed to get cache statistics: {ex.Message}" });
        }
    }

    /// <summary>
    /// Invalidates a specific cache service by name.
    /// </summary>
    [HttpPost("cache/invalidate/{cacheName}")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(InvalidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult InvalidateCache(string cacheName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cacheName))
            {
                return BadRequest(new ErrorResponse { Error = "Cache name cannot be empty" });
            }

            var cacheNames = _cacheRegistry.GetCacheNames();
            if (!cacheNames.Contains(cacheName, StringComparer.OrdinalIgnoreCase))
            {
                return NotFound(new ErrorResponse { Error = $"Cache service not found: {cacheName}" });
            }

            _cacheRegistry.InvalidateCache(cacheName);

            _logger.LogInformation("Invalidated cache");
            return Ok(new InvalidationResponse
            {
                Message = $"Successfully invalidated cache: {cacheName}",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error invalidating cache: {ex.Message}");
            return StatusCode(500, new ErrorResponse { Error = $"Failed to invalidate cache: {ex.Message}" });
        }
    }

    /// <summary>
    /// Invalidates a specific key within a cache service.
    /// </summary>
    [HttpPost("cache/invalidate/{cacheName}/key")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(typeof(InvalidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult InvalidateCacheKey(string cacheName, [FromQuery] string key)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cacheName))
            {
                return BadRequest(new ErrorResponse { Error = "Cache name cannot be empty" });
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest(new ErrorResponse { Error = "Key cannot be empty" });
            }

            var cacheNames = _cacheRegistry.GetCacheNames();
            if (!cacheNames.Contains(cacheName, StringComparer.OrdinalIgnoreCase))
            {
                return NotFound(new ErrorResponse 
                { 
                    Error = $"Cache service not found: {cacheName}" 
                });
            }

            _cacheRegistry.InvalidateCache(cacheName, key);

            _logger.LogInformation("Invalidated cache entry");
            return Ok(new InvalidationResponse
            {
                Message = $"Successfully invalidated cache entry: {cacheName}:{key}",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error invalidating cache key: {ex.Message}");
            return StatusCode(500, new ErrorResponse { Error = $"Failed to invalidate cache key: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets health status of all cache services.
    /// </summary>
    [HttpGet("health/cache")]
    [ProducesResponseType(typeof(CacheHealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public IActionResult GetCacheHealth()
    {
        try
        {
            var allStats = _cacheRegistry.GetAllStats();
            var health = new CacheHealthResponse
            {
                Timestamp = DateTime.UtcNow,
                Status = DetermineCacheHealth(allStats),
                Services = allStats.Select(s => new ServiceHealth
                {
                    ServiceName = s.CacheName,
                    IsHealthy = s.HitRate >= 0.5,
                    Utilization = s.Count / (double)s.Capacity,
                    HitRate = s.HitRate
                }).ToList()
            };

            _logger.LogInformation("Retrieved cache health status");
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error getting cache health: {ex.Message}");
            return StatusCode(500, new ErrorResponse { Error = $"Failed to get cache health: {ex.Message}" });
        }
    }

    private double CalculateAverageHitRate(IReadOnlyList<CacheServiceStats> stats)
    {
        if (stats.Count == 0)
            return 0;

        return stats.Average(s => s.HitRate);
    }

    private string DetermineCacheHealth(IReadOnlyList<CacheServiceStats> stats)
    {
        if (stats.Count == 0)
            return "Unknown";

        var averageHitRate = CalculateAverageHitRate(stats);
        
        if (averageHitRate >= 0.7)
            return "Healthy";
        else if (averageHitRate >= 0.5)
            return "Fair";
        else
            return "Poor";
    }
}

/// <summary>
/// Response model for cache statistics endpoint.
/// </summary>
public class CacheStatisticsResponse
{
    public DateTime Timestamp { get; set; }
    public List<CacheServiceStats> Services { get; set; } = new();
    public int TotalServices { get; set; }
    public double AverageHitRate { get; set; }
}

/// <summary>
/// Response model for cache health endpoint.
/// </summary>
public class CacheHealthResponse
{
    public DateTime Timestamp { get; set; }
    public string Status { get; set; } = "Unknown";
    public List<ServiceHealth> Services { get; set; } = new();
}

/// <summary>
/// Individual service health information.
/// </summary>
public class ServiceHealth
{
    public string ServiceName { get; set; } = "";
    public bool IsHealthy { get; set; }
    public double Utilization { get; set; }
    public double HitRate { get; set; }
}

/// <summary>
/// Response model for cache invalidation endpoint.
/// </summary>
public class InvalidationResponse
{
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
