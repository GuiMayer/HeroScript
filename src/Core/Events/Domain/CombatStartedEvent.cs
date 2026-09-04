using System.Collections.Immutable;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando um combate é iniciado.
/// </summary>
public record CombatStartedEvent : GameEvent
{
    private ImmutableList<string> _enemyIds = [];

    public Guid CombatId { get; init; }
    public string HeroId { get; init; } = string.Empty;
    public IReadOnlyList<string> EnemyIds
    {
        get => _enemyIds;
        init => _enemyIds = value?.ToImmutableList() ?? [];
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
