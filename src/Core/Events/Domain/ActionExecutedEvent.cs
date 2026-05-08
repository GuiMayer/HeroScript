namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma ação é executada em combate.
/// </summary>
public record ActionExecutedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public Guid ActionId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string ActionTypeName { get; init; } = string.Empty;
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public int? DamageDealt { get; init; }
    public int? EnergyChange { get; init; }
    
    public ActionExecutedEvent()
    {
        EventType = nameof(ActionExecutedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Verb = "executed";
    }
}
