using Core.Events;

namespace Core.Abstractions.Persistence;

/// <summary>
/// Optional store for operational telemetry. It is not a gameplay journal and
/// implementations must swallow/report write failures rather than affect play.
/// </summary>
public interface IOperationalEventStore
{
    /// <summary>
    /// Appends a single event to the store.
    /// Must not throw — failures are logged internally.
    /// </summary>
    Task AppendAsync(IEvent @event, CancellationToken ct = default);

    /// <summary>
    /// Returns the greatest durable GameEvent sequence, or -1 when empty.
    /// Used to preserve monotonic event identities across process restarts.
    /// </summary>
    Task<int> GetLastSequenceAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves events matching the given filter.
    /// </summary>
    Task<IReadOnlyList<IEvent>> GetEventsAsync(OperationalEventFilter filter, CancellationToken ct = default);
}
