using Core.Combat.Flow;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatFlowPolicyValidatorTests
{
    [Fact]
    public void Validate_RejectsImplicitPolicyGraph()
    {
        var result = CombatFlowPolicyValidator.Validate(new CombatFlowPoliciesDefinition());

        Assert.True(result.IsFailure);
        Assert.Contains("automatic resolution", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsUnimplementedReactionStrategy()
    {
        var policies = ValidPolicies() with
        {
            Reactions = new ReactionPolicyDefinition { Strategy = ReactionStrategy.Stack }
        };

        var result = CombatFlowPolicyValidator.Validate(policies);

        Assert.True(result.IsFailure);
        Assert.Contains("not implemented", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AcceptsExplicitImplementedPolicyGraph()
    {
        var result = CombatFlowPolicyValidator.Validate(ValidPolicies());

        Assert.True(result.IsSuccess);
    }

    private static CombatFlowPoliciesDefinition ValidPolicies() => new()
    {
        AutomaticResolution = new()
        {
            Strategy = AutomaticResolutionStrategy.ToNextPlayerInput,
            MaxAutomaticSteps = 100
        },
        ActivationOrder = new()
        {
            Strategy = ActivationOrderStrategy.RoundSnapshot,
            TieBreak = ActivationTieBreak.StableActorId
        },
        ActionBudget = new()
        {
            Strategy = ActionBudgetStrategy.ResourceLimited,
            ActionCosts = ActionCostStrategy.Configured,
            ActorScope = FlowActorScope.Player,
            ResourceId = "energy",
            ConsumingCommands = ["EXECUTE_ACTION"]
        },
        Ai = new() { Enabled = true },
        DeckCycle = new()
        {
            HandLimit = 10,
            ActorScope = FlowActorScope.Player,
            EndDiscard = DeckEndDiscardStrategy.NonRetain,
            Fatigue = FatigueStrategy.None
        },
        ResourceCycle = new()
        {
            ResourceId = "energy",
            ActorScope = FlowActorScope.All,
            StartActivation = ResourceRefreshStrategy.ResetToMax
        },
        StatusTiming = new()
        {
            Boundaries = [StatusTriggerBoundary.StartActivation],
            Ordering = StatusOrderingStrategy.PriorityThenInstanceId
        },
        Outcome = new()
        {
            EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction,
            TieBreak = OutcomeTieBreak.Draw
        },
        EncounterResolution = new() { Strategy = EncounterResolutionStrategy.ManualAck },
        Animation = new() { Mode = AnimationFrameMode.FullSnapshots },
        Journal = new() { Granularity = CombatJournalGranularity.Full },
        Reactions = new() { Strategy = ReactionStrategy.Disabled }
    };
}
