namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando recursos são recarregados.
/// </summary>
public record ResourceReloadedEvent : GameEvent
{
    public string ResourcePath { get; init; } = string.Empty;
    public string ConfigName { get; init; } = string.Empty;
    public int ResourceCount { get; init; }

    public ResourceReloadedEvent()
    {
        EventType = nameof(ResourceReloadedEvent);
        Category = EventCategory.META;
        Severity = EventSeverity.DEBUG;
        Subject = "ResourceLoader";
        Verb = "reloaded";
    }
}
