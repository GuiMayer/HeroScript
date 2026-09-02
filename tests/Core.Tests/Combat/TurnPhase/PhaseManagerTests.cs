using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Logging;
using Core.Tests.Combat.TurnOrder;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public sealed class PhaseManagerTests
{
    [Fact]
    public void TransitionAndActionValidation_UseContentPhaseIds()
    {
        var combat = TurnOrderTestHelper.CreateTestCombatState("hero", ["enemy"]);
        var sequence = new PhaseSequenceLoader(
                new ConsoleLogger(nameof(PhaseManagerTests)),
                Moq.Mock.Of<Core.Config.IConfigManager>(),
                Moq.Mock.Of<Core.Config.IResourceLoader>())
            .LoadFromJson(PhaseSequenceLoaderTests.ValidJson).Value;
        var manager = new PhaseManager(
            new PrioritySystem(new ConsoleLogger(nameof(PhaseManagerTests))),
            new ConsoleLogger(nameof(PhaseManagerTests)));

        var started = manager.StartPhase("upkeep_custom", combat, sequence);
        var transitioned = manager.TransitionToNextPhase(started.Value, sequence);

        Assert.True(transitioned.IsSuccess);
        Assert.Equal("planning_window", transitioned.Value.CurrentPhaseId);
        Assert.True(manager.CanExecuteAction(ActionType.POWER, transitioned.Value, sequence));
        Assert.False(manager.CanExecuteAction(ActionType.BASIC_ATTACK, transitioned.Value, sequence));
    }
}
