using API.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace API.Contracts;

public static class ApiProblemDetailsFactory
{
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
}
