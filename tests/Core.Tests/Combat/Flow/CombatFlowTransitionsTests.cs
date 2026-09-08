using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Run;
using Core.Tests.Combat.TurnOrder;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatFlowTransitionsTests
{
    [Theory]
    [InlineData(ActionType.PASS)]
    [InlineData(ActionType.END_TURN)]
    public void PassiveCommand_AppendsDeterministicTimelineFactWithoutAdvancingLifecycle(
        ActionType actionType)
    {
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]) with
        {
            ActivationState = new ActivationState
            {
                ActiveActorId = "hero",
                ActivationNumber = 4,
                WaitingForInput = true
            }
        };
        var command = new CombatActionCommand
        {
            ActorId = "hero",
            ActionType = actionType,
            ExpectedStep = combat.Determinism.Step
        };

        var first = CombatFlowTransitions.AppendPassiveCommand(combat, command);
        var replay = CombatFlowTransitions.AppendPassiveCommand(combat, command);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(first.Value.Determinism, replay.Value.Determinism);
        Assert.Equal(
            Assert.Single(first.Value.ActionHistory).ActionId,
            Assert.Single(replay.Value.ActionHistory).ActionId);
        Assert.Empty(combat.ActionHistory);
        var action = Assert.Single(first.Value.ActionHistory);
        Assert.Equal(actionType, action.ActionType);
        Assert.Equal("hero", action.ActorId);
        Assert.Equal(4, first.Value.ActivationState!.ActivationNumber);
        Assert.True(first.Value.ActivationState.WaitingForInput);
    }

    [Fact]
    public void PassiveCommand_RejectsActionsThatRequireUniversalEffectExecution()
    {
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]);

        var result = CombatFlowTransitions.AppendPassiveCommand(combat, new CombatActionCommand
        {
            ActorId = "hero",
            ActionType = ActionType.BASIC_ATTACK
        });

        Assert.True(result.IsFailure);
        Assert.Contains("effect executor", result.Error);
        Assert.Empty(combat.ActionHistory);
    }

    [Fact]
    public void FixedCountBudget_RejectsActionsAfterConfiguredLimit()
    {
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]) with
        {
            ActivationState = new ActivationState { ActiveActorId = "hero", ActionsTaken = 2 }
        };
        var policy = new ActionBudgetPolicyDefinition
        {
            Strategy = ActionBudgetStrategy.FixedCount,
            ActionCosts = ActionCostStrategy.Ignore,
            ActorScope = FlowActorScope.All,
            MaxActionsPerActivation = 2,
            ConsumingCommands = ["EXECUTE_ACTION"]
        };

        var result = CombatFlowTransitions.ValidateActionBudget(
            new RunState { PlayerEntityId = "hero" },
            combat,
            new CombatActionCommand { ActorId = "hero", ActionType = ActionType.BASIC_ATTACK },
            policy,
            "EXECUTE_ACTION");

        Assert.True(result.IsFailure);
        Assert.Contains("exhausted", result.Error);
    }

    [Fact]
    public void FixedCountBudget_ConsumesExactlyOneAction()
    {
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]) with
        {
            ActivationState = new ActivationState { ActiveActorId = "hero", ActionsTaken = 0 }
        };
        var command = new CombatActionCommand { ActorId = "hero", ActionType = ActionType.BASIC_ATTACK };
        var policy = new ActionBudgetPolicyDefinition
        {
            Strategy = ActionBudgetStrategy.FixedCount,
            ActionCosts = ActionCostStrategy.Ignore,
            ActorScope = FlowActorScope.All,
            MaxActionsPerActivation = 2,
            ConsumingCommands = ["EXECUTE_ACTION"]
        };

        var updated = CombatFlowTransitions.ConsumeActionBudget(
            new RunState { PlayerEntityId = "hero" },
            combat,
            command,
            policy,
            "EXECUTE_ACTION");

        Assert.Equal(0, combat.ActivationState!.ActionsTaken);
        Assert.Equal(1, updated.ActivationState!.ActionsTaken);
    }

}
