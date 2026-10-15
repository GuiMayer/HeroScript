using System.Diagnostics;
using System.Globalization;
using Core.Diagnostics;

namespace API.Middleware;

public sealed class OperationalTimingMiddleware(RequestDelegate next, IConfiguration configuration,
    ILogger<OperationalTimingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!configuration.GetValue<bool>("Performance:DiagnosticsEnabled")) { await next(context); return; }
        using var scope = OperationalTiming.Begin();
        var started = Stopwatch.GetTimestamp();
        context.Response.OnStarting(() =>
        {
            var entries = scope.Snapshot().Select(item => $"{item.Key};dur={item.Value.ToString("F2", CultureInfo.InvariantCulture)}");
            context.Response.Headers["Server-Timing"] = string.Join(", ", entries.Append(
                $"headers;dur={Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}"));
            return Task.CompletedTask;
        });
        try { await next(context); }
        finally
        {
            logger.LogInformation("HTTP diagnostic {Method} {Path} {Status}: {DurationMs}ms trace={TraceId}",
                context.Request.Method, context.Request.Path, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
        }
    }
}
