using Core.Combat.Models;
using Core.StatusEffects;

namespace Core.Run;

public sealed record RunEncounterStartCommand(
    CombatEntity InitialHero,
    IReadOnlyList<CombatEntity> InitialEnemies,
    IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? InitialStatusEffects = null);

/// <summary>
/// Combat snapshot owned by a run. Resolved encounters remain in the run so a
/// reconnect, audit or replay never depends on CombatSystem process memory.
/// </summary>
public sealed record RunEncounterState
{
    public string NodeId { get; init; } = string.Empty;
    public CombatState Combat { get; init; } = new();
    public bool Resolved { get; init; }
    public string? Outcome { get; init; }
}
