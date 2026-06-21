using Core.Events;

namespace Core.Abstractions.Events;

/// <summary>
/// Base interface for all domain events that carry causation and correlation context.
/// Enables distributed tracing and event chain reconstruction across modules.
/// </summary>
public interface ICorrelatedEvent : IEvent
{
    /// <summary>
    /// ID that groups all events belonging to the same logical operation or request.
    /// Propagated unchanged across the entire causal chain.
    /// </summary>
    Guid CorrelationId { get; }

    /// <summary>
    /// ID of the event that directly caused this event.
    /// Null for root events (externally initiated operations).
    /// </summary>
    Guid? CausationId { get; }
}
