using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Base controller with common error handling patterns
/// </summary>
public abstract class BaseApiController : ControllerBase
{
    protected readonly ILogger _logger;

    protected BaseApiController(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles common exceptions and returns appropriate HTTP responses
    /// </summary>
    protected IActionResult HandleException(Exception ex, string operation, string? resourceName = null)
    {
        var context = resourceName != null ? $"{operation} for {resourceName}" : operation;

        return ex switch
        {
            ArgumentException argEx => HandleArgumentException(argEx, context),
            InvalidOperationException invOpEx => HandleInvalidOperationException(invOpEx, context),
            DivideByZeroException divEx => HandleDivideByZeroException(divEx, context),
            _ => HandleGenericException(ex, context)
        };
    }

    private IActionResult HandleArgumentException(ArgumentException ex, string context)
    {
        _logger.LogWarning(ex, "Invalid argument: {Context}", context);
        return BadRequest(new { error = ex.Message });
    }

    private IActionResult HandleInvalidOperationException(InvalidOperationException ex, string context)
    {
        _logger.LogWarning(ex, "Invalid operation: {Context}", context);
        return BadRequest(new { error = ex.Message });
    }

    private IActionResult HandleDivideByZeroException(DivideByZeroException ex, string context)
    {
        _logger.LogWarning(ex, "Division by zero: {Context}", context);
        return BadRequest(new { error = "Division by zero", details = ex.Message });
    }

    private IActionResult HandleGenericException(Exception ex, string context)
    {
        _logger.LogError(ex, "Error: {Context}", context);
        return StatusCode(500, new { error = $"Failed to {context}", details = ex.Message });
    }
}
