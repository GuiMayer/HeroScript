using Core.Resources;

namespace Core.Events.Domain;

/// <summary>
/// Event published when a resource regenerates (e.g., at start/end of turn).
/// </summary>
public record ResourceRegeneratedEvent : GameEvent
{
    /// <summary>
    /// ID of the entity whose resource regenerated.
    /// </summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>
    /// ID of the resource that regenerated.
    /// </summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>
    /// Value before regeneration.
    /// </summary>
    public float OldValue { get; init; }

    /// <summary>
    /// Value after regeneration.
    /// </summary>
    public float NewValue { get; init; }

    /// <summary>
    /// Amount regenerated (positive for gain, negative for loss).
    /// </summary>
    public float Amount { get; init; }

    /// <summary>
    /// Timing when regeneration occurred.
    /// </summary>
    public RegenerationTiming Timing { get; init; }
}
