using API.Contracts;
using System.Text.Json;

namespace API.Middleware;

/// <summary>
/// Adds Problem Details bodies to empty failures produced before or outside
/// MVC, including routing, authorization and early-returning SSE endpoints.
/// </summary>
public sealed class ApiStatusCodeProblemDetailsMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;

    public ApiStatusCodeProblemDetailsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (context.Response.HasStarted ||
            context.Response.StatusCode < 400 ||
            context.Response.StatusCode > 599 ||
            context.Response.ContentLength.HasValue ||
            !string.IsNullOrEmpty(context.Response.ContentType))
        {
            return;
        }

        var problem = ApiProblemDetailsFactory.CreateForStatus(
            context,
            context.Response.StatusCode);
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            JsonOptions,
            context.RequestAborted);
    }
}
