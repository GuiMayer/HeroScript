using Microsoft.AspNetCore.Mvc;
using Core.Events;

namespace API.Controllers;

/// <summary>
/// Controller para consulta de eventos do sistema (Event Sourcing).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<EventsController> _logger;

    public EventsController(IEventBus eventBus, ILogger<EventsController> logger)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lista eventos da sessão atual com filtros opcionais.
    /// </summary>
    /// <param name="category">Filtrar por categoria (COMBAT, PIPELINE, META, CONFIG, REALITY_BEND)</param>
    /// <param name="severity">Filtrar por severidade (DEBUG, INFO, WARN, ANOMALY)</param>
    /// <param name="limit">Número máximo de eventos a retornar (padrão: 100)</param>
    [HttpGet]
    public IActionResult GetEvents(
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] int limit = 100)
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

            var limitedEvents = events.TakeLast(limit).ToList();

            return Ok(new
            {
                total = events.Count,
                returned = limitedEvents.Count,
                events = limitedEvents
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving events");
            return StatusCode(500, new { error = "Failed to retrieve events", details = ex.Message });
        }
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
    public IActionResult ClearHistory()
    {
        try
        {
            // TODO: Adicionar verificação de ambiente (apenas dev)
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
}
