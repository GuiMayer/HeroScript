using Microsoft.AspNetCore.Mvc;
using API.Contracts;

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
        return ApiBadRequest(ApiErrorCodes.InvalidArgument, "Invalid argument", ex.Message);
    }

    private IActionResult HandleInvalidOperationException(InvalidOperationException ex, string context)
    {
        _logger.LogWarning(ex, "Invalid operation: {Context}", context);
        return ApiBadRequest(ApiErrorCodes.InvalidOperation, "Invalid operation", ex.Message);
    }

    private IActionResult HandleDivideByZeroException(DivideByZeroException ex, string context)
    {
        _logger.LogWarning(ex, "Division by zero: {Context}", context);
        return ApiBadRequest(ApiErrorCodes.InvalidArgument, "Division by zero", ex.Message);
    }

    private IActionResult HandleGenericException(Exception ex, string context)
    {
        _logger.LogError(ex, "Error: {Context}", context);
        return ApiProblem(
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalError,
            "Internal server error",
            $"Failed to {context}");
    }

    protected BadRequestObjectResult ApiBadRequest(string code, string title, string detail)
    {
        var result = BadRequest(ApiProblemDetailsFactory.Create(
            ControllerContext.HttpContext,
            StatusCodes.Status400BadRequest,
            code,
            title,
            detail));
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    protected NotFoundObjectResult ApiNotFound(string detail)
    {
        var result = NotFound(ApiProblemDetailsFactory.Create(
            ControllerContext.HttpContext,
            StatusCodes.Status404NotFound,
            ApiErrorCodes.ResourceNotFound,
            "Resource not found",
            detail));
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    protected ObjectResult ApiProblem(
        int status,
        string code,
        string title,
        string detail,
        int? currentSequence = null,
        ulong? currentStep = null)
    {
        var result = new ObjectResult(ApiProblemDetailsFactory.Create(
            ControllerContext.HttpContext,
            status,
            code,
            title,
            detail,
            currentSequence,
            currentStep))
        {
            StatusCode = status
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
