namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma configuração é carregada.
/// </summary>
public record ConfigLoadedEvent : GameEvent
{
    public string ConfigName { get; init; } = string.Empty;
    public string? ParentConfig { get; init; }
    public int ResourcesLoaded { get; init; }

    public ConfigLoadedEvent()
    {
        EventType = nameof(ConfigLoadedEvent);
        Category = EventCategory.CONFIG;
        Severity = EventSeverity.INFO;
        Subject = "ConfigManager";
        Verb = "loaded";
    }
}
