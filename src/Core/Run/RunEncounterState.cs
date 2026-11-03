using Core.Combat.Models;
using Core.StatusEffects;

namespace Core.Run;

public sealed record RunEncounterStartCommand(
    IReadOnlyList<CombatActorState> InitialParticipants,
    IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? InitialStatusEffects = null);

/// <summary>
/// Combat snapshot owned by a run. Resolved encounters remain in the run so a
/// reconnect, audit or replay never depends on process-local combat memory.
/// </summary>
public sealed record RunEncounterState
{
    public string NodeId { get; init; } = string.Empty;
    public CombatState Combat { get; init; } = new();
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DeckLoadouts.CapturedDeckLoadout? DeckComposition { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Supports.CapturedSupportLoadout? SupportComposition { get; init; }
    public bool Resolved { get; init; }
    public string? Outcome { get; init; }
}
