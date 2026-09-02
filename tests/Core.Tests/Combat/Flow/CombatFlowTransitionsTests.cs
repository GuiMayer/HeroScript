using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Tests.Combat.TurnOrder;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatFlowTransitionsTests
{
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
            ActorScope = FlowActorScope.All,
            MaxActionsPerActivation = 2,
            ConsumingCommands = ["EXECUTE_ACTION"]
        };

        var result = CombatFlowTransitions.ValidateActionBudget(
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
            ActorScope = FlowActorScope.All,
            MaxActionsPerActivation = 2,
            ConsumingCommands = ["EXECUTE_ACTION"]
        };

        var updated = CombatFlowTransitions.ConsumeActionBudget(
            combat,
            command,
            policy,
            "EXECUTE_ACTION");

        Assert.Equal(0, combat.ActivationState!.ActionsTaken);
        Assert.Equal(1, updated.ActivationState!.ActionsTaken);
    }

    [Fact]
    public void RoundSnapshot_UsesConfiguredHeroTieBias()
    {
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy_b", "enemy_a"]);

        var order = CombatFlowTransitions.CreateRoundSnapshotOrder(
            combat,
            new ActivationOrderPolicyDefinition
            {
                Strategy = ActivationOrderStrategy.RoundSnapshot,
                TieBreak = ActivationTieBreak.EnemiesFirst
            });

        Assert.Equal(["enemy_a", "enemy_b", "hero"], order);
    }
}
