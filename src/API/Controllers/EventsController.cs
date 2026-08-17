using Microsoft.AspNetCore.Mvc;
using Core.Events;
using System.Text.Json;

namespace API.Controllers;

/// <summary>
/// Controller para consulta de eventos do sistema (Event Sourcing).
/// </summary>
[ApiController]
[Route("api/v1/admin/events")]
public class EventsController : ControllerBase
{
    private static readonly JsonSerializerOptions EventJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventsController> _logger;
    private readonly IWebHostEnvironment _environment;

    public EventsController(IEventBus eventBus, ILogger<EventsController> logger, IWebHostEnvironment environment)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// Lista eventos da sessão atual com filtros opcionais.
    /// </summary>
    /// <param name="category">Filtrar por categoria (COMBAT, PIPELINE, META, CONFIG, REALITY_BEND)</param>
    /// <param name="severity">Filtrar por severidade (DEBUG, INFO, WARN, ANOMALY)</param>
    /// <param name="limit">Número máximo de eventos a retornar (padrão: 100)</param>
    /// <param name="afterSequence">Retornar apenas GameEvents com sequência maior que esta</param>
    /// <param name="combatId">Filtrar por combatId no payload</param>
    /// <param name="runId">Filtrar por runId no payload</param>
    /// <param name="eventType">Filtrar por tipo de evento</param>
    [HttpGet]
    public IActionResult GetEvents(
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] int limit = 100,
        [FromQuery] int? afterSequence = null,
        [FromQuery] Guid? combatId = null,
        [FromQuery] Guid? runId = null,
        [FromQuery] string? eventType = null)
    {
        try
        {
            IReadOnlyList<IEvent> events;

            if (!string.IsNullOrEmpty(category) && Enum.TryParse<EventCategory>(category, true, out var cat))
            {
                events = _eventBus.GetEventHistory(cat);
            }
            else if (!string.IsNullOrEmpty(severity) && Enum.TryParse<EventSeverity>(severity, true, out var sev))
            {
                events = _eventBus.GetEventHistory(sev);
            }
            else
            {
                events = _eventBus.GetEventHistory();
            }

            var filteredEvents = ApplyEventFilters(events, afterSequence, combatId, runId, eventType).ToList();
            var limitedEvents = afterSequence.HasValue
                ? filteredEvents.Take(limit).ToList()
                : filteredEvents.TakeLast(limit).ToList();
            var lastSequence = limitedEvents.OfType<GameEvent>().Select(e => e.Sequence).DefaultIfEmpty(afterSequence ?? -1).Max();

            return Ok(new
            {
                total = filteredEvents.Count,
                returned = limitedEvents.Count,
                afterSequence,
                lastSequence,
                events = limitedEvents.Select(SerializeEvent)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving events");
            return StatusCode(500, new { error = "Failed to retrieve events", details = ex.Message });
        }
    }

    [HttpGet("~/api/v1/admin/combats/{combatId:guid}/events")]
    public IActionResult GetCombatEvents(
        Guid combatId,
        [FromQuery] int? afterSequence = null,
        [FromQuery] Guid? runId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] int limit = 100)
    {
        return GetEvents(limit: limit, afterSequence: afterSequence, combatId: combatId, runId: runId, eventType: eventType);
    }

    [HttpGet("stream")]
    public async Task StreamEvents(
        [FromQuery] int afterSequence = -1,
        [FromQuery] Guid? combatId = null,
        [FromQuery] Guid? runId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] int delayMs = 1000)
    {
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");
        Response.ContentType = "text/event-stream";

        var currentSequence = afterSequence;
        // Limit to prevent abusive long polling tight-loops
        var delay = TimeSpan.FromMilliseconds(Math.Max(delayMs, 1000));

        while (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            var events = ApplyEventFilters(_eventBus.GetEventHistory(), currentSequence, combatId, runId, eventType)
                .OfType<GameEvent>()
                .OrderBy(e => e.Sequence)
                .ToList();

            foreach (var gameEvent in events)
            {
                await Response.WriteAsync($"id: {gameEvent.Sequence}\n", HttpContext.RequestAborted);
                await Response.WriteAsync($"event: {gameEvent.EventType}\n", HttpContext.RequestAborted);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(gameEvent)}\n\n", HttpContext.RequestAborted);
                currentSequence = gameEvent.Sequence;
            }

            await Response.Body.FlushAsync(HttpContext.RequestAborted);
            await Task.Delay(delay, HttpContext.RequestAborted);
        }
    }

    [HttpGet("~/api/v1/admin/combats/{combatId:guid}/events/stream")]
    public Task StreamCombatEvents(
        Guid combatId,
        [FromQuery] int afterSequence = -1,
        [FromQuery] Guid? runId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] int delayMs = 1000)
    {
        return StreamEvents(afterSequence, combatId, runId, eventType, delayMs);
    }

    /// <summary>
    /// Obtém evento específico por ID.
    /// </summary>
    /// <param name="eventId">ID do evento</param>
    [HttpGet("{eventId}")]
    public IActionResult GetEvent(Guid eventId)
    {
        try
        {
            var @event = _eventBus.GetEventHistory()
                .FirstOrDefault(e => e.EventId == eventId);

            if (@event == null)
                return NotFound(new { error = $"Event {eventId} not found" });

            return Ok(@event);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving event {EventId}", eventId);
            return StatusCode(500, new { error = "Failed to retrieve event", details = ex.Message });
        }
    }

    /// <summary>
    /// Lista categorias de eventos disponíveis.
    /// </summary>
    [HttpGet("categories")]
    public IActionResult GetCategories()
    {
        var categories = Enum.GetNames<EventCategory>();
        return Ok(new { categories });
    }

    /// <summary>
    /// Lista severidades de eventos disponíveis.
    /// </summary>
    [HttpGet("severities")]
    public IActionResult GetSeverities()
    {
        var severities = Enum.GetNames<EventSeverity>();
        return Ok(new { severities });
    }

    /// <summary>
    /// Limpa histórico de eventos (apenas dev mode).
    /// </summary>
    [HttpDelete]
    [API.Attributes.AdminEndpoint]
    public IActionResult ClearHistory()
    {
        try
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            _eventBus.ClearHistory();
            _logger.LogInformation("Event history cleared via API");
            return Ok(new { message = "Event history cleared successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing event history");
            return StatusCode(500, new { error = "Failed to clear history", details = ex.Message });
        }
    }

    private static IEnumerable<IEvent> ApplyEventFilters(
        IReadOnlyList<IEvent> events,
        int? afterSequence,
        Guid? combatId,
        Guid? runId,
        string? eventType)
    {
        return events.Where(e =>
            MatchesSequence(e, afterSequence) &&
            MatchesEventType(e, eventType) &&
            MatchesPayloadGuid(e, "combatId", combatId) &&
            MatchesPayloadGuid(e, "runId", runId));
    }

    private static JsonElement SerializeEvent(IEvent @event) =>
        JsonSerializer.SerializeToElement(@event, @event.GetType(), EventJsonOptions);

    private static bool MatchesSequence(IEvent @event, int? afterSequence)
    {
        return !afterSequence.HasValue || @event is GameEvent gameEvent && gameEvent.Sequence > afterSequence.Value;
    }

    private static bool MatchesEventType(IEvent @event, string? eventType)
    {
        return string.IsNullOrWhiteSpace(eventType) || string.Equals(@event.EventType, eventType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPayloadGuid(IEvent @event, string key, Guid? expected)
    {
        if (!expected.HasValue)
            return true;
        if (@event is not GameEvent gameEvent)
            return false;

        var eventProperty = @event.GetType().GetProperty(
            key,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.IgnoreCase);
        if (eventProperty?.GetValue(@event) is Guid eventGuid)
            return eventGuid == expected.Value;

        if (!gameEvent.Payload.TryGetValue(key, out var value))
            return false;

        return value switch
        {
            Guid guid => guid == expected.Value,
            string text => Guid.TryParse(text, out var parsed) && parsed == expected.Value,
            _ => string.Equals(value?.ToString(), expected.Value.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }
}
