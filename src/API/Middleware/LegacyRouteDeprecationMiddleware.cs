namespace API.Middleware;

/// <summary>
/// Announces that historical unversioned API routes are compatibility adapters.
/// The header is deliberately observational: it never changes a route's
/// response, preserving existing clients while they migrate to the v1 contract.
/// </summary>
public sealed class LegacyRouteDeprecationMiddleware
{
    // 2026-08-16T00:00:00Z, encoded as an RFC 9745 Structured Field Date.
    public const string DeprecationDate = "@1786838400";

    private readonly RequestDelegate _next;

    public LegacyRouteDeprecationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsLegacyApiRoute(context.Request.Path))
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                response.Headers["Deprecation"] = DeprecationDate;
                response.Headers["Link"] = "</openapi/v1.json>; rel=\"successor-version\"";
                return Task.CompletedTask;
            }, context.Response);
        }

        await _next(context);
    }

    private static bool IsLegacyApiRoute(PathString path) =>
        path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase);
}
