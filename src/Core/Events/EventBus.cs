namespace Core.Events;

using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Logging;

/// <summary>
/// Implementação thread-safe do EventBus com histórico para Event Sourcing.
/// Supports optional dual-write to an IEventStore for durable persistence.
/// </summary>
public class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<IEvent> _eventHistory = new();
    private readonly object _lock = new();
    private readonly ILogger _logger;
    private readonly IEventStore? _eventStore;
    private int _sequenceCounter = 0;

    public EventBus(ILogger logger, IEventStore? eventStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventStore = eventStore;
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
                var updatedEvent = gameEvent with
                {
                    Sequence = sequence,
                    EventId = gameEvent.EventId == Guid.Empty
                        ? DeterministicId.Create(0UL, checked((ulong)sequence), $"event:{gameEvent.EventType}")
                        : gameEvent.EventId,
                    Timestamp = gameEvent.Timestamp == DateTime.UnixEpoch
                        ? DateTime.UnixEpoch.AddTicks(sequence)
                        : gameEvent.Timestamp
                };
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

        _logger.LogDebug($"Published event: {typeof(TEvent).Name} (ID: {eventForHandlers.EventId})");

        // Fire-and-forget dual write to durable store (failure is logged, not propagated)
        if (_eventStore != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _eventStore.AppendAsync(eventForHandlers).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogError($"EventStore append failed for {typeof(TEvent).Name}: {ex.Message}", ex); }
            });
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
            _sequenceCounter = 0;
        }

        _logger.LogInformation("Event history cleared");
    }
}
