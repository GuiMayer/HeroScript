using System.Collections.Immutable;
using Core.Effects;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma ação é executada em combate.
/// </summary>
public record ActionExecutedEvent : GameEvent
{
    private ImmutableArray<EffectApplicationRecord> _applications = [];

    public Guid CombatId { get; init; }
    public Guid ActionId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string ActionTypeName { get; init; } = string.Empty;
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public IReadOnlyList<EffectApplicationRecord> Applications
    {
        get => _applications;
        init => _applications = value?.ToImmutableArray() ?? [];
    }
    
    public ActionExecutedEvent()
    {
        EventType = nameof(ActionExecutedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Verb = "executed";
    }
}
