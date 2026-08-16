using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Determinism;

namespace Core.Run;

public sealed record RunState
{
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public string ConfigName { get; init; } = "default";
    public string PlayerEntityId { get; init; } = "player";
    public int Gold { get; init; }
    public int PowerPoints { get; init; }
    public string? CurrentNodeId { get; init; }
    public RunMapState Map { get; init; } = new();
    public Guid? ActiveEncounterId { get; init; }
    public ImmutableArray<RunEncounterState> Encounters { get; init; } = [];
    public DeckState Deck { get; init; } = new();
    public ImmutableArray<CardSelectionState> CardSelections { get; init; } = [];
    public ImmutableArray<ShopState> Shops { get; init; } = [];
    public ImmutableArray<PreparationState> Preparations { get; init; } = [];
    public ImmutableArray<RunRelicState> Relics { get; init; } = [];
    public ImmutableDictionary<string, JsonElement> Metadata { get; init; } =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    public ContentManifest? ContentManifest { get; init; }
    public DeterministicContext Determinism { get; init; } =
        DeterministicContext.Create(0, "legacy");

    public RunEncounterState? GetActiveEncounter()
    {
        return ActiveEncounterId is { } encounterId
            ? Encounters.FirstOrDefault(encounter => encounter.Combat.CombatId == encounterId)
            : null;
    }

    public RunEncounterState? GetEncounter(Guid combatId)
    {
        return Encounters.FirstOrDefault(encounter => encounter.Combat.CombatId == combatId);
    }
}
