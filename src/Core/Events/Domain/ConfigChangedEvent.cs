namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando a configuração atual é trocada.
/// </summary>
public record ConfigChangedEvent : GameEvent
{
    public string OldConfig { get; init; } = string.Empty;
    public string NewConfig { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;

    public ConfigChangedEvent()
    {
        EventType = nameof(ConfigChangedEvent);
        Category = EventCategory.CONFIG;
        Severity = EventSeverity.INFO;
        Subject = "ConfigManager";
        Verb = "changed";
    }
}
