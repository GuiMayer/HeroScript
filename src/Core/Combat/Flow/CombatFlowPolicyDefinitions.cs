using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;

namespace Core.Combat.Flow;

/// <summary>
/// Complete, immutable policy graph used by the canonical combat loop. Every
/// gameplay-relevant choice is explicit so a run never depends on a C# fallback.
/// </summary>
public sealed record CombatFlowPoliciesDefinition
{
    public AutomaticResolutionPolicyDefinition AutomaticResolution { get; init; } = new();
    public ActivationOrderPolicyDefinition ActivationOrder { get; init; } = new();
    public ActionBudgetPolicyDefinition ActionBudget { get; init; } = new();
    public DeckCyclePolicyDefinition DeckCycle { get; init; } = new();
    public ResourceCyclePolicyDefinition ResourceCycle { get; init; } = new();
    public StatusTimingPolicyDefinition StatusTiming { get; init; } = new();
    public OutcomePolicyDefinition Outcome { get; init; } = new();
    public EncounterResolutionPolicyDefinition EncounterResolution { get; init; } = new();
    public AnimationPolicyDefinition Animation { get; init; } = new();
    public JournalPolicyDefinition Journal { get; init; } = new();
    public ReactionPolicyDefinition Reactions { get; init; } = new();
}

public sealed record AutomaticResolutionPolicyDefinition
{
    public AutomaticResolutionStrategy Strategy { get; init; }
    public int MaxAutomaticSteps { get; init; }
}

public sealed record ActivationOrderPolicyDefinition
{
    public ActivationOrderStrategy Strategy { get; init; }
    public ActivationTieBreak TieBreak { get; init; }
}

public sealed record ActionBudgetPolicyDefinition
{
    private ImmutableArray<string> _consumingCommands = [];

    public ActionBudgetStrategy Strategy { get; init; }
    public string? ResourceId { get; init; }
    public int? MaxActionsPerActivation { get; init; }
    public IReadOnlyList<string> ConsumingCommands
    {
        get => _consumingCommands;
        init => _consumingCommands = value?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray() ?? [];
    }
}

public sealed record DeckCyclePolicyDefinition
{
    private ImmutableArray<string> _retainTags = [];

    public int DrawPerActivation { get; init; }
    public int HandLimit { get; init; }
    public DeckEndDiscardStrategy EndDiscard { get; init; }
    public IReadOnlyList<string> RetainTags
    {
        get => _retainTags;
        init => _retainTags = value?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray() ?? [];
    }
    public string? EtherealTag { get; init; }
    public bool ShuffleDiscardWhenDrawEmpty { get; init; }
    public bool AllowPartialDraw { get; init; }
    public FatigueStrategy Fatigue { get; init; }
}

public sealed record ResourceCyclePolicyDefinition
{
    public string ResourceId { get; init; } = string.Empty;
    public ResourceRefreshStrategy StartActivation { get; init; }
    public float? Amount { get; init; }
}

public sealed record StatusTimingPolicyDefinition
{
    private ImmutableArray<StatusTriggerBoundary> _boundaries = [];

    public IReadOnlyList<StatusTriggerBoundary> Boundaries
    {
        get => _boundaries;
        init => _boundaries = value?.Distinct().ToImmutableArray() ?? [];
    }
    public StatusOrderingStrategy Ordering { get; init; }
}

public sealed record OutcomePolicyDefinition
{
    public OutcomeEvaluationBoundary EvaluationBoundary { get; init; }
    public OutcomeTieBreak TieBreak { get; init; }
}

public sealed record EncounterResolutionPolicyDefinition
{
    public EncounterResolutionStrategy Strategy { get; init; }
}

public sealed record AnimationPolicyDefinition
{
    public AnimationFrameMode Mode { get; init; }
}

public sealed record JournalPolicyDefinition
{
    public CombatJournalGranularity Granularity { get; init; }
}

public sealed record ReactionPolicyDefinition
{
    public ReactionStrategy Strategy { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutomaticResolutionStrategy { Unspecified, ToNextPlayerInput }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationOrderStrategy { Unspecified, RoundSnapshot }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivationTieBreak { Unspecified, StableActorId, HeroesFirst, EnemiesFirst, SeededRandom }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionBudgetStrategy { Unspecified, ResourceLimited, FixedCount }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeckEndDiscardStrategy { Unspecified, None, All, NonRetain, DownToHandLimit }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FatigueStrategy { Unspecified, None }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceRefreshStrategy { Unspecified, ResetToMax, Add, Preserve, Set }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusTriggerBoundary { StartActivation, EndActivation, StartRound, EndRound }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusOrderingStrategy { Unspecified, PriorityThenInstanceId }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutcomeEvaluationBoundary { Unspecified, Immediate, AfterCurrentAction, AfterResolutionStack }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutcomeTieBreak { Unspecified, Draw, HeroesWin, EnemiesWin, ActiveActorWins }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterResolutionStrategy { Unspecified, ManualAck, Automatic }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationFrameMode { Unspecified, FullSnapshots, CompactWithSnapshotLookup }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatJournalGranularity { Unspecified, Full }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionStrategy { Unspecified, Disabled, Immediate, Stack }

public static class CombatFlowPolicyValidator
{
    public static Result Validate(CombatFlowPoliciesDefinition policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        if (policies.AutomaticResolution.Strategy != AutomaticResolutionStrategy.ToNextPlayerInput)
            return Result.Failure("Only automatic resolution strategy 'ToNextPlayerInput' is implemented");
        if (policies.AutomaticResolution.MaxAutomaticSteps is < 1 or > 10_000)
            return Result.Failure("Automatic resolution maxAutomaticSteps must be between 1 and 10000");
        if (policies.ActivationOrder.Strategy != ActivationOrderStrategy.RoundSnapshot)
            return Result.Failure("Only activation order strategy 'RoundSnapshot' is implemented");
        if (policies.ActivationOrder.TieBreak == ActivationTieBreak.Unspecified)
            return Result.Failure("Activation order tieBreak is required");
        if (policies.ActionBudget.Strategy == ActionBudgetStrategy.Unspecified)
            return Result.Failure("Action budget strategy is required");
        if (policies.ActionBudget.Strategy == ActionBudgetStrategy.ResourceLimited &&
            string.IsNullOrWhiteSpace(policies.ActionBudget.ResourceId))
            return Result.Failure("Resource-limited action budget requires resourceId");
        if (policies.ActionBudget.Strategy == ActionBudgetStrategy.FixedCount &&
            policies.ActionBudget.MaxActionsPerActivation is not > 0)
            return Result.Failure("Fixed-count action budget requires a positive maxActionsPerActivation");
        if (policies.ActionBudget.ConsumingCommands.Count == 0)
            return Result.Failure("Action budget consumingCommands cannot be empty");
        if (policies.DeckCycle.DrawPerActivation < 0 || policies.DeckCycle.HandLimit < 1)
            return Result.Failure("Deck cycle draw and hand limits are invalid");
        if (policies.DeckCycle.EndDiscard == DeckEndDiscardStrategy.Unspecified ||
            policies.DeckCycle.Fatigue == FatigueStrategy.Unspecified)
            return Result.Failure("Deck cycle discard and fatigue strategies are required");
        if (string.IsNullOrWhiteSpace(policies.ResourceCycle.ResourceId) ||
            policies.ResourceCycle.StartActivation == ResourceRefreshStrategy.Unspecified)
            return Result.Failure("Resource cycle resourceId and startActivation strategy are required");
        if (policies.ResourceCycle.StartActivation is ResourceRefreshStrategy.Add or ResourceRefreshStrategy.Set &&
            policies.ResourceCycle.Amount is null)
            return Result.Failure("Resource cycle Add/Set strategy requires amount");
        if (policies.StatusTiming.Boundaries.Count == 0 ||
            policies.StatusTiming.Ordering == StatusOrderingStrategy.Unspecified)
            return Result.Failure("Status timing boundaries and ordering are required");
        if (policies.Outcome.EvaluationBoundary == OutcomeEvaluationBoundary.Unspecified ||
            policies.Outcome.TieBreak == OutcomeTieBreak.Unspecified)
            return Result.Failure("Outcome evaluation boundary and tieBreak are required");
        if (policies.EncounterResolution.Strategy == EncounterResolutionStrategy.Unspecified)
            return Result.Failure("Encounter resolution strategy is required");
        if (policies.Animation.Mode == AnimationFrameMode.Unspecified)
            return Result.Failure("Animation frame mode is required");
        if (policies.Journal.Granularity != CombatJournalGranularity.Full)
            return Result.Failure("Only full combat journal granularity is implemented");
        if (policies.Reactions.Strategy != ReactionStrategy.Disabled)
            return Result.Failure($"Reaction strategy '{policies.Reactions.Strategy}' is not implemented");

        return Result.Success();
    }
}
