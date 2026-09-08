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
    private readonly IRunQueryService _runs;
    private static readonly JsonSerializerOptions StreamJsonOptions = new(JsonSerializerDefaults.Web);

    public DurableEventsController(
        IRunEventProjectionReader events,
        IRunQueryService runs,
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
        [FromQuery] int? afterFactIndex = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var invalid = ValidateCursor(afterSequence, afterFactIndex, limit);
        if (invalid != null)
            return invalid;
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);

        var cursor = CreateCursor(afterSequence, afterFactIndex);
        var events = await _events.ReadRunEventsAsync(runId, cursor, limit, cancellationToken);
        return Ok(ToEnvelope(runId, null, cursor, events));
    }

    [HttpGet("api/v1/combats/{combatId:guid}/events")]
    public async Task<IActionResult> GetCombatEvents(
        Guid combatId,
        [FromQuery] int afterSequence = 0,
        [FromQuery] int? afterFactIndex = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var invalid = ValidateCursor(afterSequence, afterFactIndex, limit);
        if (invalid != null)
            return invalid;
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
            return ApiNotFound(run.Error);

        var events = await _events.ReadCombatEventsAsync(
            run.Value.RunId,
            combatId,
            CreateCursor(afterSequence, afterFactIndex),
            limit,
            cancellationToken);
        return Ok(ToEnvelope(
            run.Value.RunId,
            combatId,
            CreateCursor(afterSequence, afterFactIndex),
            events));
    }

    [HttpGet("api/v1/runs/{runId:guid}/events/stream")]
    public async Task StreamRunEvents(
        Guid runId,
        [FromQuery] int? afterSequence = null,
        [FromQuery] int? afterFactIndex = null,
        [FromQuery] int delayMs = 1000)
    {
        var run = _runs.GetRun(runId);
        if (run.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cursor = ResolveStreamCursor(afterSequence, afterFactIndex);
        await Stream(
            cursor,
            delayMs,
            (after, ct) => _events.ReadRunEventsAsync(runId, after, 100, ct));
    }

    [HttpGet("api/v1/combats/{combatId:guid}/events/stream")]
    public async Task StreamCombatEvents(
        Guid combatId,
        [FromQuery] int? afterSequence = null,
        [FromQuery] int? afterFactIndex = null,
        [FromQuery] int delayMs = 1000)
    {
        var run = _runs.GetRunByCombat(combatId);
        if (run.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var cursor = ResolveStreamCursor(afterSequence, afterFactIndex);
        await Stream(
            cursor,
            delayMs,
            (after, ct) => _events.ReadCombatEventsAsync(run.Value.RunId, combatId, after, 100, ct));
    }

    private async Task Stream(
        RunEventCursor initialCursor,
        int delayMs,
        Func<RunEventCursor, CancellationToken, Task<IReadOnlyList<RunProjectionEvent>>> read)
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
                await Response.WriteAsync($"id: {@event.Sequence}:{@event.FactIndex}\n", HttpContext.RequestAborted);
                await Response.WriteAsync($"event: {@event.EventType}\n", HttpContext.RequestAborted);
                await Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(@event, StreamJsonOptions)}\n\n",
                    HttpContext.RequestAborted);
                cursor = new RunEventCursor(@event.Sequence, @event.FactIndex);
            }

            await Response.Body.FlushAsync(HttpContext.RequestAborted);
            await Task.Delay(delay, HttpContext.RequestAborted);
        }
    }

    private RunEventCursor ResolveStreamCursor(int? querySequence, int? queryFactIndex)
    {
        var header = Request.Headers["Last-Event-ID"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
        {
            var parts = header.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var factIndex))
                return new RunEventCursor(sequence, factIndex);
        }
        return CreateCursor(querySequence ?? 0, queryFactIndex);
    }

    private IActionResult? ValidateCursor(int afterSequence, int? afterFactIndex, int limit)
    {
        return afterSequence < 0 || afterFactIndex is < -1 || limit is < 1 or > 1000
            ? ApiBadRequest(
                ApiErrorCodes.InvalidRequest,
                "Invalid event cursor",
                "afterSequence must be non-negative, afterFactIndex at least -1, and limit between 1 and 1000")
            : null;
    }

    private static RunEventCursor CreateCursor(int afterSequence, int? afterFactIndex) =>
        afterFactIndex.HasValue
            ? new RunEventCursor(afterSequence, afterFactIndex.Value)
            : RunEventCursor.AfterSequence(afterSequence);

    private static object ToEnvelope(
        Guid runId,
        Guid? combatId,
        RunEventCursor cursor,
        IReadOnlyList<RunProjectionEvent> events)
    {
        return new
        {
            runId,
            combatId,
            afterSequence = cursor.Sequence,
            afterFactIndex = cursor.FactIndex == int.MaxValue ? (int?)null : cursor.FactIndex,
            lastSequence = events.LastOrDefault()?.Sequence ?? cursor.Sequence,
            lastFactIndex = events.LastOrDefault()?.FactIndex ?? cursor.FactIndex,
            nextCursor = events.LastOrDefault() is { } last
                ? new RunEventCursor(last.Sequence, last.FactIndex).ToString()
                : cursor.ToString(),
            returned = events.Count,
            events
        };
    }
}
