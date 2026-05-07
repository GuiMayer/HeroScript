using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Health check endpoint for monitoring API availability
/// </summary>
[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Simple health check endpoint
    /// </summary>
    /// <returns>Health status</returns>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
}
