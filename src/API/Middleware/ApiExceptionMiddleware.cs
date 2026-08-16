using API.Contracts;

namespace API.Middleware;

/// <summary>
/// Last-resort exception boundary for failures outside controller handlers.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            _logger.LogError(exception, "Unhandled API exception");
            var problem = ApiProblemDetailsFactory.Create(
                context,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "Internal server error",
                _environment.IsDevelopment()
                    ? exception.Message
                    : "The request could not be completed.");

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
    }
}
