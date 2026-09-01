using API.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace API.Contracts;

public static class ApiProblemDetailsFactory
{
    public static ProblemDetails CreateForStatus(
        HttpContext? context,
        int status,
        string? detail = null)
    {
        var descriptor = Describe(status);
        return Create(
            context,
            status,
            descriptor.Code,
            descriptor.Title,
            string.IsNullOrWhiteSpace(detail) ? descriptor.Detail : detail);
    }

    public static ProblemDetails Create(
        HttpContext? context,
        int status,
        string code,
        string title,
        string detail,
        int? currentSequence = null,
        ulong? currentStep = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context?.Request.Path.Value,
            Type = $"https://httpstatuses.com/{status}"
        };

        AddExtensions(problem, context, code, currentSequence, currentStep);
        return problem;
    }

    public static ValidationProblemDetails CreateValidation(
        HttpContext context,
        IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request validation failed",
            Detail = "One or more request fields are invalid.",
            Instance = context.Request.Path.Value,
            Type = "https://httpstatuses.com/400"
        };

        AddExtensions(problem, context, ApiErrorCodes.InvalidRequest);
        return problem;
    }

    private static void AddExtensions(
        ProblemDetails problem,
        HttpContext? context,
        string code,
        int? currentSequence = null,
        ulong? currentStep = null)
    {
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.GetCorrelationId(context);

        if (currentSequence.HasValue)
            problem.Extensions["currentSequence"] = currentSequence.Value;
        if (currentStep.HasValue)
            problem.Extensions["currentStep"] = currentStep.Value;
    }

    private static (string Code, string Title, string Detail) Describe(int status) => status switch
    {
        StatusCodes.Status400BadRequest => (
            ApiErrorCodes.InvalidRequest,
            "Invalid request",
            "The request could not be accepted."),
        StatusCodes.Status401Unauthorized => (
            ApiErrorCodes.Unauthorized,
            "Authentication required",
            "Valid credentials are required for this endpoint."),
        StatusCodes.Status403Forbidden => (
            ApiErrorCodes.Forbidden,
            "Forbidden",
            "The operation is not allowed."),
        StatusCodes.Status404NotFound => (
            ApiErrorCodes.ResourceNotFound,
            "Resource not found",
            "The requested resource was not found."),
        StatusCodes.Status405MethodNotAllowed => (
            ApiErrorCodes.MethodNotAllowed,
            "Method not allowed",
            "The HTTP method is not supported by this endpoint."),
        StatusCodes.Status409Conflict => (
            ApiErrorCodes.VersionConflict,
            "Conflict",
            "The request conflicts with the current resource state."),
        StatusCodes.Status415UnsupportedMediaType => (
            ApiErrorCodes.UnsupportedMediaType,
            "Unsupported media type",
            "The request content type is not supported."),
        StatusCodes.Status422UnprocessableEntity => (
            ApiErrorCodes.RuleViolation,
            "Rule violation",
            "The request violates a game rule."),
        StatusCodes.Status429TooManyRequests => (
            ApiErrorCodes.RateLimited,
            "Too many requests",
            "The request rate limit was exceeded."),
        StatusCodes.Status501NotImplemented => (
            ApiErrorCodes.NotImplemented,
            "Not implemented",
            "The requested capability is not implemented."),
        StatusCodes.Status503ServiceUnavailable => (
            ApiErrorCodes.DependencyUnavailable,
            "Service unavailable",
            "A required dependency is unavailable."),
        _ => (
            ApiErrorCodes.InternalError,
            "Internal server error",
            "The request could not be completed.")
    };
}
