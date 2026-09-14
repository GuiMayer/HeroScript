namespace Core.Events.Domain;

/// <summary>
/// Best-effort operational record for an authoring reload transaction. It is
/// deliberately outside the deterministic run journal.
/// </summary>
public sealed record ContentReloadedEvent : GameEvent
{
    public string SettingId { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public string? Revision { get; init; }
    public string? Error { get; init; }
    public int WarningCount { get; init; }
    public int InvalidatedCacheCount { get; init; }

    public ContentReloadedEvent()
    {
        EventType = nameof(ContentReloadedEvent);
        Category = EventCategory.META;
        Subject = "ContentAuthoring";
        Verb = "reload";
    }
}
