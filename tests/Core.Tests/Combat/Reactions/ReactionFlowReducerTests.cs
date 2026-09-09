using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Combat.Reactions;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Combat.Reactions;

public sealed class ReactionFlowReducerTests
{
    private readonly ReactionFlowReducer _reducer = new();

    [Theory]
    [InlineData(ReactionStackOrder.Lifo, "second")]
    [InlineData(ReactionStackOrder.Fifo, "first")]
    public void Pass_AllEligibleActors_SelectsConfiguredStackEnd(
        ReactionStackOrder order,
        string expectedPendingId)
    {
        var policy = Policy(order);
        var combat = Combat();
        combat = _reducer.Propose(combat, Pending("first", "hero", 1), policy).Value;
        combat = _reducer.Propose(combat, Pending("second", "enemy", 2), policy).Value;

        var first = _reducer.Pass(combat, combat.PriorityWindow!.HolderActorId, policy);
        var second = _reducer.Pass(first.Value.Combat,
            first.Value.Combat.PriorityWindow!.HolderActorId, policy);

        Assert.Null(first.Value.ActionToResolve);
        Assert.Equal(expectedPendingId, second.Value.ActionToResolve!.PendingActionId);
        Assert.Single(second.Value.Combat.PendingActions);
        Assert.NotNull(second.Value.Combat.PriorityWindow);
        Assert.Equal(2, second.Value.Combat.ActionHistory.Count(action =>
            action.ActionType == ActionType.PASS_PRIORITY));
    }

    [Fact]
    public void Propose_RejectsConfiguredDepthWithoutChangingPreviousSnapshot()
    {
        var policy = Policy(ReactionStackOrder.Lifo) with { MaxStackDepth = 1 };
        var original = Combat();
        var first = _reducer.Propose(original, Pending("first", "hero", 1), policy).Value;

        var overflow = _reducer.Propose(first, Pending("second", "enemy", 2), policy);

        Assert.True(overflow.IsFailure);
        Assert.Empty(original.PendingActions);
        Assert.Single(first.PendingActions);
    }

    [Fact]
    public void CompleteResolution_ReopensPriorityForRemainingAction()
    {
        var policy = Policy(ReactionStackOrder.Lifo);
        var combat = _reducer.Propose(Combat(), Pending("first", "hero", 1), policy).Value;
        combat = _reducer.Propose(combat, Pending("second", "enemy", 2), policy).Value;
        var firstPass = _reducer.Pass(combat, combat.PriorityWindow!.HolderActorId, policy).Value;
        var selected = _reducer.Pass(firstPass.Combat,
            firstPass.Combat.PriorityWindow!.HolderActorId, policy).Value;

        var completed = _reducer.CompleteResolution(selected.Combat, selected.ActionToResolve!, policy);

        Assert.True(completed.IsSuccess);
        Assert.NotNull(completed.Value.PriorityWindow);
        Assert.Equal(0, completed.Value.PriorityWindow!.ConsecutivePasses);
        Assert.Equal("first", Assert.Single(completed.Value.PendingActions).PendingActionId);
    }

    private static PendingActionState Pending(string id, string actorId, int depth) => new()
    {
        PendingActionId = id,
        Depth = depth,
        ContentRevision = "revision",
        Command = new CombatActionCommand { ActorId = actorId, ActionType = ActionType.POWER }
    };

    private static ReactionPolicyDefinition Policy(ReactionStackOrder order) => new()
    {
        Strategy = ReactionStrategy.PriorityStack,
        StackOrder = order,
        Eligibility = ReactionActorEligibility.AllAlive,
        TargetLock = ReactionLockTiming.Proposal,
        CostTiming = ReactionCostTiming.Resolution,
        Failure = ReactionResolutionFailure.FizzleKeepPaid,
        MaxStackDepth = 8,
        ReopenAfterResolution = true
    };

    private static CombatState Combat()
    {
        var hero = Actor("hero", "player");
        var enemy = Actor("enemy", "opposition");
        return new CombatState
        {
            CombatId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Actors = new[] { hero, enemy }.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
            ActorOrder = ["hero", "enemy"],
            Determinism = DeterministicContext.Create(7, "revision")
        };
    }

    private static CombatActorState Actor(string id, string sideId) => new()
    {
        InstanceId = id,
        DefinitionId = id,
        ContentRevision = "revision",
        SideId = sideId,
        ControllerBinding = new ControllerBinding
        {
            Kind = id == "hero" ? ControllerKind.Player : ControllerKind.AI,
            PolicyId = id == "hero" ? null : "gambit"
        }
    };
}
