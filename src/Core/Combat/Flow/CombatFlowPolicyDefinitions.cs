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
    public ActionBudgetPolicyDefinition ActionBudget { get; init; } = new();
    public AiTurnPolicyDefinition Ai { get; init; } = new();
    public DeckCyclePolicyDefinition DeckCycle { get; init; } = new();
    public ResourceCyclePolicyDefinition ResourceCycle { get; init; } = new();
    public StatusTimingPolicyDefinition StatusTiming { get; init; } = new();
    public OutcomePolicyDefinition Outcome { get; init; } = new();
    public EncounterResolutionPolicyDefinition EncounterResolution { get; init; } = new();
    public AnimationPolicyDefinition Animation { get; init; } = new();
    public JournalPolicyDefinition Journal { get; init; } = new();
    public ReactionPolicyDefinition Reactions { get; init; } = new();
}

public sealed record AiTurnPolicyDefinition
{
    private ImmutableArray<string> _decisionIds = [];

    public bool Enabled { get; init; }
    public bool AutoEndAfterAction { get; init; } = true;
    public bool PublishIntents { get; init; } = true;
    public IntentPolicyDefinition Intent { get; init; } = new();
    public IReadOnlyList<string> DecisionIds
    {
        get => _decisionIds;
        init => _decisionIds = value?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray() ?? [];
    }
}

public sealed record IntentPolicyDefinition
{
    public IntentRefreshStrategy Refresh { get; init; }
    public InvalidIntentStrategy WhenInvalid { get; init; }
}

public sealed record AutomaticResolutionPolicyDefinition
{
    public AutomaticResolutionStrategy Strategy { get; init; }
    public int MaxAutomaticSteps { get; init; }
}

public sealed record ActionBudgetPolicyDefinition
{
    private ImmutableArray<string> _consumingCommands = [];

    public ActionBudgetStrategy Strategy { get; init; }
    public ActionCostStrategy ActionCosts { get; init; }
    public FlowActorScope ActorScope { get; init; }
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
    public int InitialPlayableCardCount { get; init; }
    public FlowActorScope ActorScope { get; init; }
    public EncounterDeckStartStrategy EncounterStart { get; init; }
    public EncounterDeckCleanupStrategy EncounterCleanup { get; init; }
    public ExhaustPersistenceStrategy ExhaustPersistence { get; init; }
    public GeneratedCardPersistenceStrategy GeneratedCardPersistence { get; init; }
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
    public FlowActorScope ActorScope { get; init; }
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
    private ImmutableArray<string> _openingActionTags = [];
    private ImmutableArray<string> _responseActionTags = [];

    public ReactionStrategy Strategy { get; init; }
    public ReactionStackOrder StackOrder { get; init; }
    public ReactionActorEligibility Eligibility { get; init; }
    public ReactionLockTiming TargetLock { get; init; }
    public ReactionCostTiming CostTiming { get; init; }
    public ReactionResolutionFailure Failure { get; init; }
    public int MaxStackDepth { get; init; }
    public bool ReopenAfterResolution { get; init; }
    public IReadOnlyList<string> OpeningActionTags
    {
        get => _openingActionTags;
        init => _openingActionTags = NormalizeTags(value);
    }
    public IReadOnlyList<string> ResponseActionTags
    {
        get => _responseActionTags;
        init => _responseActionTags = NormalizeTags(value);
    }

    private static ImmutableArray<string> NormalizeTags(IEnumerable<string>? value) => value?
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
        .ToImmutableArray() ?? [];
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AutomaticResolutionStrategy { Unspecified, ToNextPlayerInput }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionBudgetStrategy { Unspecified, ResourceLimited, FixedCount }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionCostStrategy { Unspecified, Configured, Ignore }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlowActorScope { Unspecified, RunOwner, PlayerControlled, AiControlled, All }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeckEndDiscardStrategy { Unspecified, None, All, NonRetain, DownToHandLimit }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterDeckStartStrategy { Unspecified, PreserveZones, ResetOrdered, ResetShuffled }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterDeckCleanupStrategy { Unspecified, PreserveZones, ReturnToDrawPile }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExhaustPersistenceStrategy { Unspecified, Encounter, Run }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GeneratedCardPersistenceStrategy { Unspecified, Encounter, Run }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FatigueStrategy { Unspecified, None }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceRefreshStrategy { Unspecified, ResetToMax, Add, Preserve, Set }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusTriggerBoundary { Unspecified, StartActivation, EndActivation, StartRound, EndRound }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StatusOrderingStrategy { Unspecified, PriorityThenInstanceId }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutcomeEvaluationBoundary { Unspecified, Immediate, AfterCurrentAction, AfterResolutionStack }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutcomeTieBreak { Unspecified, Draw, PlayerControlledWins, AiControlledWins, ActiveActorWins }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterResolutionStrategy { Unspecified, ManualAck, Automatic }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnimationFrameMode { Unspecified, FullSnapshots, CompactWithSnapshotLookup }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CombatJournalGranularity { Unspecified, Full }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionStrategy { Unspecified, Disabled, Automatic, PriorityStack }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionStackOrder { Unspecified, Lifo, Fifo }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionActorEligibility { Unspecified, AllAlive, OpponentsOnly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionLockTiming { Unspecified, Proposal, Resolution }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionCostTiming { Unspecified, Proposal, Resolution }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReactionResolutionFailure { Unspecified, RejectTransaction, FizzleKeepPaid, FizzleRefund }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntentRefreshStrategy { Unspecified, RecomputeOnPublish, LockUntilActorActivation }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvalidIntentStrategy { Unspecified, Fail, Recompute, Hide }

public static class CombatFlowPolicyValidator
{
    public static Result Validate(CombatFlowPoliciesDefinition policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        if (policies.AutomaticResolution.Strategy != AutomaticResolutionStrategy.ToNextPlayerInput)
            return Result.Failure("Only automatic resolution strategy 'ToNextPlayerInput' is implemented");
        if (policies.AutomaticResolution.MaxAutomaticSteps is < 1 or > 10_000)
            return Result.Failure("Automatic resolution maxAutomaticSteps must be between 1 and 10000");
        if (policies.ActionBudget.Strategy == ActionBudgetStrategy.Unspecified)
            return Result.Failure("Action budget strategy is required");
        if (policies.ActionBudget.ActionCosts == ActionCostStrategy.Unspecified)
            return Result.Failure("Action budget actionCosts strategy is required");
        if (policies.ActionBudget.ActorScope == FlowActorScope.Unspecified)
            return Result.Failure("Action budget actorScope is required");
        if (policies.ActionBudget.Strategy == ActionBudgetStrategy.ResourceLimited &&
            string.IsNullOrWhiteSpace(policies.ActionBudget.ResourceId))
            return Result.Failure("Resource-limited action budget requires resourceId");
        if (policies.ActionBudget.Strategy == ActionBudgetStrategy.FixedCount &&
            policies.ActionBudget.MaxActionsPerActivation is not > 0)
            return Result.Failure("Fixed-count action budget requires a positive maxActionsPerActivation");
        if (policies.ActionBudget.ConsumingCommands.Count == 0)
            return Result.Failure("Action budget consumingCommands cannot be empty");
        if (!policies.Ai.Enabled)
            return Result.Failure("ToNextPlayerInput automatic resolution requires AI processing to be enabled");
        if (policies.Ai.DecisionIds.Count == 0)
            return Result.Failure("AI decisionIds cannot be empty");
        if (policies.Ai.Intent.Refresh == IntentRefreshStrategy.Unspecified ||
            policies.Ai.Intent.WhenInvalid == InvalidIntentStrategy.Unspecified)
            return Result.Failure("AI intent refresh and invalidation policies are required");
        if (policies.DeckCycle.DrawPerActivation < 0 ||
            policies.DeckCycle.HandLimit < 1 ||
            policies.DeckCycle.InitialPlayableCardCount < 0 ||
            policies.DeckCycle.InitialPlayableCardCount > policies.DeckCycle.HandLimit)
            return Result.Failure("Deck cycle draw and hand limits are invalid");
        if (policies.DeckCycle.ActorScope == FlowActorScope.Unspecified)
            return Result.Failure("Deck cycle actorScope is required");
        if (policies.DeckCycle.EndDiscard == DeckEndDiscardStrategy.Unspecified ||
            policies.DeckCycle.Fatigue == FatigueStrategy.Unspecified)
            return Result.Failure("Deck cycle discard and fatigue strategies are required");
        if (policies.DeckCycle.EncounterStart == EncounterDeckStartStrategy.Unspecified ||
            policies.DeckCycle.EncounterCleanup == EncounterDeckCleanupStrategy.Unspecified ||
            policies.DeckCycle.ExhaustPersistence == ExhaustPersistenceStrategy.Unspecified ||
            policies.DeckCycle.GeneratedCardPersistence == GeneratedCardPersistenceStrategy.Unspecified)
        {
            return Result.Failure(
                "Deck encounter start, cleanup, exhaust and generated-card persistence strategies are required");
        }
        if (string.IsNullOrWhiteSpace(policies.ResourceCycle.ResourceId) ||
            policies.ResourceCycle.StartActivation == ResourceRefreshStrategy.Unspecified ||
            policies.ResourceCycle.ActorScope == FlowActorScope.Unspecified)
            return Result.Failure("Resource cycle resourceId, actorScope and startActivation strategy are required");
        if (policies.ResourceCycle.StartActivation is ResourceRefreshStrategy.Add or ResourceRefreshStrategy.Set &&
            policies.ResourceCycle.Amount is null)
            return Result.Failure("Resource cycle Add/Set strategy requires amount");
        if (policies.StatusTiming.Boundaries.Count == 0 ||
            policies.StatusTiming.Boundaries.Contains(StatusTriggerBoundary.Unspecified) ||
            policies.StatusTiming.Ordering == StatusOrderingStrategy.Unspecified)
            return Result.Failure("Status timing boundaries and ordering are required");
        if (policies.Outcome.EvaluationBoundary == OutcomeEvaluationBoundary.Unspecified)
            return Result.Failure("Outcome evaluationBoundary is required");
        if (policies.Outcome.TieBreak == OutcomeTieBreak.Unspecified)
            return Result.Failure("Outcome tieBreak is required");
        if (policies.EncounterResolution.Strategy != EncounterResolutionStrategy.ManualAck)
            return Result.Failure("Only encounter resolution strategy 'ManualAck' is implemented");
        if (policies.Animation.Mode == AnimationFrameMode.Unspecified)
            return Result.Failure("Animation frame mode is required");
        if (policies.Journal.Granularity != CombatJournalGranularity.Full)
            return Result.Failure("Only full combat journal granularity is implemented");
        if (policies.Reactions.Strategy == ReactionStrategy.Unspecified)
            return Result.Failure("Reaction strategy is required");
        if (policies.Reactions.Strategy == ReactionStrategy.PriorityStack)
        {
            if (policies.Reactions.StackOrder == ReactionStackOrder.Unspecified ||
                policies.Reactions.Eligibility == ReactionActorEligibility.Unspecified ||
                policies.Reactions.TargetLock == ReactionLockTiming.Unspecified ||
                policies.Reactions.CostTiming == ReactionCostTiming.Unspecified ||
                policies.Reactions.Failure == ReactionResolutionFailure.Unspecified)
            {
                return Result.Failure(
                    "PriorityStack requires explicit stackOrder, eligibility, targetLock, costTiming and failure policies");
            }
            if (policies.Reactions.MaxStackDepth is < 1 or > 1_000)
                return Result.Failure("PriorityStack maxStackDepth must be between 1 and 1000");
            if (!policies.Reactions.ReopenAfterResolution)
                return Result.Failure(
                    "PriorityStack must reopen priority after each resolution; automatic whole-stack resolution is not supported");
        }
        if (policies.Reactions.Strategy != ReactionStrategy.PriorityStack &&
            policies.Outcome.EvaluationBoundary == OutcomeEvaluationBoundary.AfterResolutionStack)
        {
            return Result.Failure(
                "AfterResolutionStack outcome evaluation requires the PriorityStack reaction strategy");
        }

        return Result.Success();
    }
}
