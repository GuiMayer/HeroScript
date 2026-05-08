namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando a energia do herói muda.
/// </summary>
public record EnergyChangedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public int OldEnergy { get; init; }
    public int NewEnergy { get; init; }
    public new int Delta { get; init; }
    public string Reason { get; init; } = string.Empty;
    
    public EnergyChangedEvent()
    {
        EventType = nameof(EnergyChangedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.DEBUG;
        Subject = "EnergyPool";
        Verb = "changed";
    }
}
