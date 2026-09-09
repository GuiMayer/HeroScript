using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Combat.Modifiers;
using Core.Determinism;
using Core.Run.Sandbox;
using Core.Resources;
using Core.Run.Branching;

namespace Core.Run;

public sealed record RunState
{
    private ImmutableDictionary<Guid, CombatResolutionRecord> _combatResolutions =
        ImmutableDictionary<Guid, CombatResolutionRecord>.Empty;
    private ImmutableList<string> _completedActivityNodeIds = [];
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public RunLifecycleState Lifecycle { get; init; } = RunLifecycleState.Active;
    public string ConfigName { get; init; } = "default";
    public string SettingId { get; init; } = "default";
    public string PlayerEntityId { get; init; } = "player";
    public string? ModeId { get; init; }
    public ResolvedGameMode? ResolvedMode { get; init; }
    public string? ChallengeId { get; init; }
    public CombatScenarioDefinition? Scenario { get; init; }
    public string? ScenarioHash { get; init; }
    public string? AttemptKey { get; init; }
    public RunLineage? Lineage { get; init; }
    public ResourceSet ResourceState { get; init; } = new();

    public string? CurrentNodeId { get; init; }
    public RunMapState Map { get; init; } = new();
    public Guid? ActiveEncounterId { get; init; }
    public ImmutableArray<RunEncounterState> Encounters { get; init; } = [];
    public DeckState Deck { get; init; } = new();
    public ImmutableArray<CardSelectionState> CardSelections { get; init; } = [];
    public ImmutableArray<ShopState> Shops { get; init; } = [];
    public ImmutableArray<PreparationState> Preparations { get; init; } = [];
    public IReadOnlyList<string> CompletedActivityNodeIds
    {
        get => _completedActivityNodeIds;
        init => _completedActivityNodeIds = value?.ToImmutableList() ?? [];
    }
    public ImmutableArray<RunRelicState> Relics { get; init; } = [];
    public ImmutableArray<ScriptModifierInstance> Modifiers { get; init; } = [];
    public IReadOnlyDictionary<Guid, CombatResolutionRecord> CombatResolutions
    {
        get => _combatResolutions;
        init => _combatResolutions = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<Guid, CombatResolutionRecord>.Empty;
    }
    public ImmutableDictionary<string, JsonElement> Metadata { get; init; } =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    public ContentManifest? ContentManifest { get; init; }
    public DeterministicContext Determinism { get; init; } =
        DeterministicContext.Create(0, "uninitialized");

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

    public CombatResolutionRecord? GetCombatResolution(Guid commandId) =>
        _combatResolutions.TryGetValue(commandId, out var resolution) ? resolution : null;

}
