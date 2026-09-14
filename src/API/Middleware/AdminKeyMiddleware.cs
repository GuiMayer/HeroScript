using API.Attributes;
using API.Services;

namespace API.Middleware;

public sealed class AdminKeyMiddleware
{
    public const string HeaderName = "X-Admin-Key";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public AdminKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task InvokeAsync(HttpContext context, IToolAccessPolicy toolAccess)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<AdminEndpointAttribute>() == null)
        {
            await _next(context);
            return;
        }

        var enabled = _configuration.GetValue<bool>("Admin:Enabled", false);
        if (!enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var expectedKey = _configuration["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedKey) ||
            !context.Request.Headers.TryGetValue(HeaderName, out var providedKey) ||
            !string.Equals(providedKey.ToString(), expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!toolAccess.Allows(ToolCapabilities.AdminOperations))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}
