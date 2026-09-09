using System.Collections.Immutable;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando um combate é iniciado.
/// </summary>
public record CombatStartedEvent : GameEvent
{
    private ImmutableList<string> _participantIds = [];

    public Guid CombatId { get; init; }
    public IReadOnlyList<string> ParticipantIds
    {
        get => _participantIds;
        init => _participantIds = value?.ToImmutableList() ?? [];
    }
    public CombatStartedEvent()
    {
        EventType = nameof(CombatStartedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.INFO;
        Subject = "CombatSystem";
        Verb = "started";
    }
}
