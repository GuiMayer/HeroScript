namespace Core.Events;

using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Logging;

/// <summary>
/// Thread-safe operational telemetry bus with optional best-effort persistence.
/// Gameplay recovery and replay exclusively use RunCommit.
/// </summary>
public class OperationalEventBus : IOperationalEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<IEvent> _eventHistory = new();
    private readonly object _lock = new();
    private readonly ILogger _logger;
    private readonly IOperationalEventStore? _eventStore;
    private readonly IGameEventContextAccessor? _contextAccessor;
    private int _sequenceCounter = 0;

    public OperationalEventBus(
        ILogger logger,
        IOperationalEventStore? eventStore = null,
        IGameEventContextAccessor? contextAccessor = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventStore = eventStore;
        _contextAccessor = contextAccessor;
        if (_eventStore != null)
        {
            _sequenceCounter = checked(
                _eventStore.GetLastSequenceAsync().ConfigureAwait(false).GetAwaiter().GetResult() + 1);
        }
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        List<Delegate>? handlersToInvoke = null;
        var eventForHandlers = @event;

        lock (_lock)
        {
            // Atualizar sequence se for GameEvent antes de adicionar ao histórico
            if (@event is GameEvent gameEvent)
            {
                var sequence = _sequenceCounter++;
                var context = ResolveContext(gameEvent);
                var updatedEvent = gameEvent with
                {
                    Sequence = sequence,
                    Context = context,
                    EventId = gameEvent.EventId == Guid.Empty
                        ? DeterministicId.Create(0UL, checked((ulong)sequence), $"event:{gameEvent.EventType}")
                        : gameEvent.EventId,
                    Timestamp = gameEvent.Timestamp == DateTime.UnixEpoch
                        ? DateTime.UnixEpoch.AddTicks(sequence)
                        : gameEvent.Timestamp
                };

                // Durable observability is written in the same serialization
                // order as the in-memory history. The run journal remains the
                // authoritative transactional source for gameplay transitions.
                _eventStore?.AppendAsync(updatedEvent)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                _eventHistory.Add(updatedEvent);
                eventForHandlers = (TEvent)(IEvent)updatedEvent;
            }
            else
            {
                _eventHistory.Add(@event);
            }

            // Copiar handlers para invocar fora do lock
            var eventType = typeof(TEvent);
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlersToInvoke = new List<Delegate>(handlers);
            }
        }

        // Invocar handlers fora do lock para evitar deadlocks
        if (handlersToInvoke != null)
        {
            foreach (var handler in handlersToInvoke)
            {
                try
                {
                    ((Action<TEvent>)handler)(eventForHandlers);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error invoking event handler for {typeof(TEvent).Name}: {ex.Message}", ex);
                }
            }
        }

        if (eventForHandlers is GameEvent published)
        {
            _logger.LogDebug(
                $"Published event {published.EventType} sequence={published.Sequence} " +
                $"eventId={published.EventId} runId={published.Context.RunId} " +
                $"combatId={published.Context.CombatId} commandId={published.Context.CommandId} " +
                $"traceId={published.Context.TraceId}");
        }
        else
        {
            _logger.LogDebug($"Published event: {typeof(TEvent).Name} (ID: {eventForHandlers.EventId})");
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        var eventType = typeof(TEvent);

        lock (_lock)
        {
            if (!_handlers.ContainsKey(eventType))
            {
                _handlers[eventType] = new List<Delegate>();
            }
            _handlers[eventType].Add(handler);
        }

        _logger.LogDebug($"Subscribed to event: {typeof(TEvent).Name}");

        return new EventSubscription(() => Unsubscribe(eventType, handler));
    }

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        lock (_lock)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _handlers.Remove(eventType);
                }
            }
        }

        _logger.LogDebug($"Unsubscribed from event: {eventType.Name}");
    }

    public IReadOnlyList<IEvent> GetEventHistory()
    {
        lock (_lock)
        {
            return _eventHistory.ToList();
        }
    }

    public IReadOnlyList<IEvent> GetEventHistory(EventCategory category)
    {
        lock (_lock)
        {
            return _eventHistory
                .OfType<GameEvent>()
                .Where(e => e.Category == category)
                .Cast<IEvent>()
                .ToList();
        }
    }

    public IReadOnlyList<IEvent> GetEventHistory(EventSeverity severity)
    {
        lock (_lock)
        {
            return _eventHistory
                .OfType<GameEvent>()
                .Where(e => e.Severity == severity)
                .Cast<IEvent>()
                .ToList();
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _eventHistory.Clear();
            if (_eventStore == null)
                _sequenceCounter = 0;
        }

        _logger.LogInformation("Event history cleared");
    }

    private GameEventContext ResolveContext(GameEvent gameEvent)
    {
        var context = _contextAccessor?.Current ?? GameEventContext.Empty;
        context = context.Merge(DiscoverContext(gameEvent));
        return context.Merge(gameEvent.Context);
    }

    private static GameEventContext DiscoverContext(GameEvent gameEvent) => new()
    {
        RunId = ReadGuid(gameEvent, "RunId") ?? ReadPayloadGuid(gameEvent, "runId"),
        CombatId = ReadGuid(gameEvent, "CombatId") ?? ReadPayloadGuid(gameEvent, "combatId"),
        CommandId = ReadGuid(gameEvent, "CommandId") ?? ReadPayloadGuid(gameEvent, "commandId"),
        ContentRevision = ReadString(gameEvent, "ContentRevision") ?? string.Empty
    };

    private static Guid? ReadGuid(object source, string propertyName)
    {
        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value switch
        {
            Guid guid when guid != Guid.Empty => guid,
            string text when Guid.TryParse(text, out var guid) && guid != Guid.Empty => guid,
            _ => null
        };
    }

    private static string? ReadString(object source, string propertyName) =>
        source.GetType().GetProperty(propertyName)?.GetValue(source) as string;

    private static Guid? ReadPayloadGuid(GameEvent gameEvent, string key)
    {
        if (!gameEvent.Payload.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            Guid guid when guid != Guid.Empty => guid,
            string text when Guid.TryParse(text, out var guid) && guid != Guid.Empty => guid,
            _ => null
        };
    }
}
