namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando um combate é finalizado.
/// </summary>
public record CombatEndedEvent : GameEvent
{
    public Guid CombatId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int TotalTurns { get; init; }
    public int TotalActions { get; init; }
    public TimeSpan Duration { get; init; }
    
    public CombatEndedEvent()
    {
        EventType = nameof(CombatEndedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Subject = "CombatSystem";
        Verb = "ended";
    }
}
