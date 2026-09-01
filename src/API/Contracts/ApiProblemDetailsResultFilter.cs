using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Contracts;

/// <summary>
/// Normalizes legacy controller errors at the HTTP boundary. Domain and
/// controller code can migrate incrementally while clients always receive one
/// RFC 9457-compatible representation.
/// </summary>
public sealed class ApiProblemDetailsResultFilter : IAsyncResultFilter
{
    private readonly IWebHostEnvironment _environment;

    public ApiProblemDetailsResultFilter(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        var status = ResolveStatus(context.Result);
        if (status is >= 400 and <= 599)
        {
            var existing = (context.Result as ObjectResult)?.Value;
            if (existing is not ProblemDetails)
            {
                var detail = status >= 500 && !_environment.IsDevelopment()
                    ? null
                    : ExtractText(existing);
                var problem = ApiProblemDetailsFactory.CreateForStatus(
                    context.HttpContext,
                    status.Value,
                    detail);
                PreserveValidationErrors(problem, existing);

                var normalized = new ObjectResult(problem) { StatusCode = status };
                normalized.ContentTypes.Add("application/problem+json");
                context.Result = normalized;
            }
            else if (context.Result is ObjectResult problemResult)
            {
                problemResult.ContentTypes.Clear();
                problemResult.ContentTypes.Add("application/problem+json");
            }
        }

        await next();
    }

    private static int? ResolveStatus(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        ForbidResult => StatusCodes.Status403Forbidden,
        ChallengeResult => StatusCodes.Status401Unauthorized,
        _ => null
    };

    private static string? ExtractText(object? value)
    {
        if (value == null)
            return null;
        if (value is string text)
            return text;

        var type = value.GetType();
        var error = ReadProperty(type, value, "Error");
        var details = ReadProperty(type, value, "Details");
        var message = ReadProperty(type, value, "Message");
        return Join(error, details, message);
    }

    private static void PreserveValidationErrors(ProblemDetails problem, object? value)
    {
        if (value == null)
            return;
        var errors = value.GetType().GetProperties()
            .FirstOrDefault(property =>
                string.Equals(property.Name, "Errors", StringComparison.OrdinalIgnoreCase))
            ?.GetValue(value);
        if (errors != null)
            problem.Extensions["errors"] = errors;
    }

    private static string? ReadProperty(Type type, object value, string name) =>
        type.GetProperties()
            .FirstOrDefault(property =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.GetValue(value)?.ToString();

    private static string? Join(params string?[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(" ", parts);
    }
}
