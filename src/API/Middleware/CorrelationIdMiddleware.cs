namespace API.Middleware;

using Core.Events;

/// <summary>
/// Middleware that reads or generates a correlation ID for each HTTP request,
/// attaches it to the response header and injects it into the log scope so all
/// log messages produced during the request carry the ID automatically.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemName = "ApiCorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private readonly IGameEventContextAccessor _eventContext;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger,
        IGameEventContextAccessor eventContext)
    {
        _next = next;
        _logger = logger;
        _eventContext = eventContext;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Reuse incoming correlation ID or generate a new one
        var suppliedCorrelationId = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = string.IsNullOrWhiteSpace(suppliedCorrelationId) || suppliedCorrelationId.Length > 128
            ? context.TraceIdentifier
            : suppliedCorrelationId.Trim();

        // Propagate to response so callers can trace end-to-end
        context.Response.Headers[HeaderName] = correlationId;
        context.Items[ItemName] = correlationId;

        // Inject into the log scope so every log line in this request has CorrelationId
        using (_eventContext.Push(new GameEventContext { TraceId = correlationId }))
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    public static string GetCorrelationId(HttpContext? context)
    {
        if (context?.Items.TryGetValue(ItemName, out var value) == true && value is string correlationId)
            return correlationId;

        return context?.Request.Headers[HeaderName].FirstOrDefault()
            ?? context?.TraceIdentifier
            ?? string.Empty;
    }
}
