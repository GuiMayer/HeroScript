using System.Globalization;
using System.Text.Json;
using API.Contracts;
using Core.Run;
using Core.Run.Events;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
public sealed class DurableEventsController : BaseApiController
{
    private readonly IRunEventProjectionReader _events;
    private readonly IRunManager _runs;
    private static readonly JsonSerializerOptions StreamJsonOptions = new(JsonSerializerDefaults.Web);

    public DurableEventsController(
        IRunEventProjectionReader events,
        IRunManager runs,
        ILogger<DurableEventsController> logger)
        : base(logger)
    {
        _events = events;
        _runs = runs;
    }

    [HttpGet("api/v1/runs/{runId:guid}/events")]
    public async Task<IActionResult> GetRunEvents(
        Guid runId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var invalid = ValidateCursor(afterSequence, limit);
        if (invalid != null)
            return invalid;
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);

        var events = await _events.ReadRunEventsAsync(runId, afterSequence, limit, cancellationToken);
        return Ok(ToEnvelope(runId, null, afterSequence, events));
    }

    [HttpGet("api/v1/combats/{combatId:guid}/events")]
    public async Task<IActionResult> GetCombatEvents(
        Guid combatId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var invalid = ValidateCursor(afterSequence, limit);
        if (invalid != null)
            return invalid;
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);

        var events = await _events.ReadCombatEventsAsync(
            run.Value.RunId,
            combatId,
            afterSequence,
            limit,
            cancellationToken);
        return Ok(ToEnvelope(run.Value.RunId, combatId, afterSequence, events));
    }

    [HttpGet("api/v1/runs/{runId:guid}/events/stream")]
    public async Task StreamRunEvents(
        Guid runId,
        [FromQuery] int? afterSequence = null,
        [FromQuery] int delayMs = 1000)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cursor = ResolveStreamCursor(afterSequence);
        await Stream(
            cursor,
            delayMs,
            (after, ct) => _events.ReadRunEventsAsync(runId, after, 100, ct));
    }

    [HttpGet("api/v1/combats/{combatId:guid}/events/stream")]
    public async Task StreamCombatEvents(
        Guid combatId,
        [FromQuery] int? afterSequence = null,
        [FromQuery] int delayMs = 1000)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cursor = ResolveStreamCursor(afterSequence);
        await Stream(
            cursor,
            delayMs,
            (after, ct) => _events.ReadCombatEventsAsync(run.Value.RunId, combatId, after, 100, ct));
    }

    private async Task Stream(
        int initialCursor,
        int delayMs,
        Func<int, CancellationToken, Task<IReadOnlyList<RunProjectionEvent>>> read)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";
        var cursor = initialCursor;
        var delay = TimeSpan.FromMilliseconds(System.Math.Clamp(delayMs, 250, 30_000));

        while (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            var events = await read(cursor, HttpContext.RequestAborted);
            foreach (var @event in events)
            {
                await Response.WriteAsync($"id: {@event.Sequence}\n", HttpContext.RequestAborted);
                await Response.WriteAsync($"event: {@event.EventType}\n", HttpContext.RequestAborted);
                await Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(@event, StreamJsonOptions)}\n\n",
                    HttpContext.RequestAborted);
                cursor = @event.Sequence;
            }

            await Response.Body.FlushAsync(HttpContext.RequestAborted);
            await Task.Delay(delay, HttpContext.RequestAborted);
        }
    }

    private int ResolveStreamCursor(int? queryCursor)
    {
        var header = Request.Headers["Last-Event-ID"].FirstOrDefault();
        var headerCursor = int.TryParse(header, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        return System.Math.Max(queryCursor ?? 0, headerCursor);
    }

    private IActionResult? ValidateCursor(int afterSequence, int limit)
    {
        return afterSequence < 0 || limit is < 1 or > 1000
            ? ApiBadRequest(
                ApiErrorCodes.InvalidRequest,
                "Invalid event cursor",
                "afterSequence must be non-negative and limit must be between 1 and 1000")
            : null;
    }

    private static object ToEnvelope(
        Guid runId,
        Guid? combatId,
        int afterSequence,
        IReadOnlyList<RunProjectionEvent> events)
    {
        return new
        {
            runId,
            combatId,
            afterSequence,
            lastSequence = events.LastOrDefault()?.Sequence ?? afterSequence,
            returned = events.Count,
            events
        };
    }
}

