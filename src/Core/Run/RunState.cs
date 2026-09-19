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
    // Absent until the first conversation, preserving hashes of snapshots without narrative state.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Dialogue.RunNarrativeState? Narrative { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public ImmutableArray<Dialogue.DialogueState> Dialogues
    {
        get => Narrative?.Dialogues ?? [];
        init => Narrative = (Narrative ?? new()) with { Dialogues = value };
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public ImmutableDictionary<string, string> NarrativeFlags
    {
        get => Narrative?.Flags ?? ImmutableDictionary<string, string>.Empty;
        init => Narrative = (Narrative ?? new()) with { Flags = value };
    }
    public IReadOnlyList<string> CompletedActivityNodeIds
    {
        get => _completedActivityNodeIds;
        init => _completedActivityNodeIds = value?.ToImmutableList() ?? [];
    }
    public ImmutableArray<RunRelicState> Relics { get; init; } = [];
    public ImmutableArray<ScriptModifierInstance> Modifiers { get; init; } = [];
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

}
