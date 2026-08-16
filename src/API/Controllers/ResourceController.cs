using API.Models;
using Core.Math;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Controller for resource management and cache operations
/// </summary>
[ApiController]
[Route("api/resource")]
public class ResourceController : ControllerBase
{
    private readonly ILogger<ResourceController> _logger;
    private readonly ConfigReloadSettings _reloadSettings;
    private readonly IMathEngine _mathEngine;

    public ResourceController(ILogger<ResourceController> logger, ConfigReloadSettings reloadSettings, IMathEngine mathEngine)
    {
        _logger = logger;
        _reloadSettings = reloadSettings;
        _mathEngine = mathEngine;
    }

    /// <summary>
    /// Get origin information for formula resources
    /// </summary>
    /// <param name="path">Resource path (optional, defaults to "Pipelines/MathFormulas.json")</param>
    /// <returns>Resource origin information</returns>
    [HttpGet("origins")]
    [ProducesResponseType(typeof(ResourceOriginDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult GetResourceOrigins([FromQuery] string? path = null)
    {
        try
        {
            // Default to MathFormulas if no path specified
            var resourcePath = path ?? "Pipelines/MathFormulas.json";

            // Currently only MathFormulas are tracked
            if (!resourcePath.Contains("MathFormulas", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new { error = $"Resource '{resourcePath}' not found or not tracked" });
            }

            var origins = _mathEngine.GetFormulaOrigins();

            var result = new ResourceOriginDto
            {
                ResourcePath = resourcePath,
                Origins = origins
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting resource origins for path {Path}", path);
            return StatusCode(500, new { error = "Failed to get resource origins", details = ex.Message });
        }
    }

    /// <summary>
    /// Reload formula resources (admin operation - requires ALLOW_CONFIG_RELOAD flag)
    /// </summary>
    /// <param name="path">Resource path (optional, if empty reloads all formulas)</param>
    /// <returns>Success message</returns>
    [HttpPost("reload")]
    [API.Attributes.AdminEndpoint]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public IActionResult ReloadResource([FromQuery] string? path = null)
    {
        // Check if resource reload is enabled
        if (!_reloadSettings.Enabled)
        {
            return StatusCode(403, new
            {
                error = "Resource reload is disabled",
                details = "Set ALLOW_CONFIG_RELOAD=true in environment or appsettings.json to enable this operation"
            });
        }

        try
        {
            // Log admin operation
            _logger.LogWarning("Reloading resources (admin operation). Path: {Path}", path ?? "all");

            // Invalidate formula cache
            _mathEngine.InvalidateCache();

            var message = string.IsNullOrEmpty(path)
                ? "All formula resources reloaded successfully"
                : $"Resource '{path}' reloaded successfully";

            _logger.LogInformation("Resources reloaded successfully");
            return Ok(new { message, timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading resources");
            return StatusCode(500, new { error = "Failed to reload resources", details = ex.Message });
        }
    }

    /// <summary>
    /// Get cache statistics (not implemented - returns 501)
    /// </summary>
    /// <returns>Cache statistics</returns>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(CacheStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult GetCacheStats()
    {
        // Cache statistics are not currently tracked in the Core
        return StatusCode(501, new
        {
            error = "Cache statistics not implemented",
            details = "The Core does not currently expose cache statistics"
        });
    }
}
