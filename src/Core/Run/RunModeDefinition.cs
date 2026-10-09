using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Combat.Flow;
using Core.Combat.TurnOrder;
using Core.Calculations;
using Core.CardZones;

namespace Core.Run;

public sealed record GameModeDefinition
{
    private ImmutableDictionary<string, JsonElement> _rules =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<string> _cardPoolIds = [];
    private ImmutableArray<string> _enemyPoolIds = [];
    private ImmutableArray<string> _calculationPipelineIds = [];
    private ImmutableArray<ContextualInfluenceDefinition> _influences = [];

    public string ModeId { get; init; } = string.Empty;
    public string? RunDefinitionId { get; init; }
    public bool AllowCustomSeed { get; init; } = true;
    public string? FlowRulesId { get; init; }
    public string? CombatRulesId { get; init; }
    public string? ReplayPolicyId { get; init; }
    public string? TimelinePolicyId { get; init; }
    public string? ContentBindingPolicyId { get; init; }
    public string? CapabilityPolicyId { get; init; }
    public string? ProgressionPolicyId { get; init; }
    public string? ActorResourceLifecyclePolicyId { get; init; }
    public string? CardZoneSystemId { get; init; }

    public IReadOnlyList<string> CalculationPipelineIds
    {
        get => _calculationPipelineIds;
        init => _calculationPipelineIds = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ContextualInfluenceDefinition> Influences
    {
        get => _influences;
        init => _influences = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<string> CardPoolIds
    {
        get => _cardPoolIds;
        init => _cardPoolIds = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<string> EnemyPoolIds
    {
        get => _enemyPoolIds;
        init => _enemyPoolIds = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyDictionary<string, JsonElement> Rules
    {
        get => _rules;
        init => _rules = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

/// <summary>
/// Immutable game-flow settings selected by a game mode. The engine owns the
/// interpretation; JSON only selects compatible, validated policies.
/// </summary>
public sealed record FlowRulesDefinition
{
    public string FlowRulesId { get; init; } = string.Empty;
    public bool AllowMapNavigation { get; init; } = true;
}

public sealed record CombatRulesDefinition
{
    public string CombatRulesId { get; init; } = string.Empty;
    public string? DefaultPhaseSequenceId { get; init; }
    public TurnOrderPolicyDefinition TurnOrder { get; init; } = new();
    public CombatFlowPoliciesDefinition Flow { get; init; } = new();
}

public sealed record ReplayPolicyDefinition
{
    public string ReplayPolicyId { get; init; } = string.Empty;
    public bool SemanticVerification { get; init; }
    public TimelineAccessLevel TimelineAccess { get; init; } = TimelineAccessLevel.Summary;
    public bool AllowHistoricalInspection { get; init; }
    public bool AllowForkFromHistory { get; init; }
    public bool AllowHeadRestore { get; init; }
}

public sealed record TimelinePolicyDefinition
{
    public string TimelinePolicyId { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool GroupByTurn { get; init; }
    public int MaxItemsPerPage { get; init; } = 200;
}

public sealed record ContentBindingPolicyDefinition
{
    public string ContentBindingPolicyId { get; init; } = string.Empty;
    public ActiveRunContentBinding ActiveRuns { get; init; } = ActiveRunContentBinding.Pinned;
    public ContentActivationBoundary ActivationBoundary { get; init; } = ContentActivationBoundary.OutsideCombat;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimelineAccessLevel
{
    None,
    Summary,
    Full
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActiveRunContentBinding
{
    Pinned,
    Versioned
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContentActivationBoundary
{
    OutsideCombat
}

public sealed record CapabilityPolicyDefinition
{
    public string CapabilityPolicyId { get; init; } = string.Empty;
    public bool AllowScenarioAuthoring { get; init; }
    public bool AllowCustomDeck { get; init; }
    public bool AllowInitialEffects { get; init; }
    public bool AllowResourceOverrides { get; init; }
    public bool AllowTimelineFork { get; init; }
    public bool AllowCombatSimulation { get; init; }
    public bool AllowHotReloadActivation { get; init; }
    public bool AllowRunResourceCheats { get; init; }
    public bool AllowCardZoneCheats { get; init; }
    public InspectionDetailLevel CardInspectionDetail { get; init; } = InspectionDetailLevel.Resolved;
    public int MaxCards { get; init; } = 100;
    public int MaxActors { get; init; } = 6;
    public int MaxBranchesPerRoot { get; init; } = 50;
    public int MaxSimulationCommands { get; init; } = 100;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InspectionDetailLevel
{
    Disabled,
    Resolved,
    Full
}

public sealed record EnemyPoolDefinition
{
    private ImmutableArray<string> _entityDefinitionIds = [];

    public string EnemyPoolId { get; init; } = string.Empty;
    public IReadOnlyList<string> EntityDefinitionIds
    {
        get => _entityDefinitionIds;
        init => _entityDefinitionIds = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>
/// The resolved immutable policy graph captured by a run. It makes the
/// effective interpretation of a mode visible to clients and replay.
/// </summary>
public sealed record ResolvedGameMode
{
    public GameModeDefinition Definition { get; init; } = new();
    public FlowRulesDefinition FlowRules { get; init; } = new();
    public CombatRulesDefinition CombatRules { get; init; } = new();
    public ReplayPolicyDefinition ReplayPolicy { get; init; } = new();
    public TimelinePolicyDefinition TimelinePolicy { get; init; } = new();
    public ContentBindingPolicyDefinition ContentBindingPolicy { get; init; } = new();
    public CapabilityPolicyDefinition CapabilityPolicy { get; init; } = new();
    public RunProgressionPolicyDefinition ProgressionPolicy { get; init; } = new();
    public ActorResourceLifecyclePolicyDefinition? ActorResourceLifecyclePolicy { get; init; }
    public CardZoneSystemDefinition? CardZoneSystem { get; init; }
}

public sealed record DailyChallengeDefinition
{
    private ImmutableDictionary<string, float> _scoreResourceWeights =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public string ChallengeId { get; init; } = string.Empty;
    public bool IsCurrent { get; init; }
    public string ConfigName { get; init; } = "default";
    public string RunDefinitionId { get; init; } = "default_run";
    public string ModeId { get; init; } = "standard";
    public ulong Seed { get; init; }
    public IReadOnlyDictionary<string, float> ScoreResourceWeights
    {
        get => _scoreResourceWeights;
        init => _scoreResourceWeights = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
}
