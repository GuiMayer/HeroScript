using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Models;
using Core.Combat.Reactions;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class AutomaticFlowDriverTests
{
    [Fact]
    public void Drive_StopsWithoutSideEffectsWhenPlayerInputIsRequired()
    {
        var flow = new Mock<ICombatFlowPlanner>();
        var decisions = new Mock<IDecisionPolicyRegistry>();
        var commands = new Mock<ICombatCommandHandler>();
        var run = Run(maximumSteps: 10);
        var combat = Combat("hero", waitingForInput: true);
        var driver = new AutomaticFlowDriver(flow.Object, decisions.Object, commands.Object);

        var result = driver.Drive(new AutomaticFlowRequest
        {
            CombatId = combat.CombatId,
            Run = run,
            Combat = combat
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.Steps);
        Assert.Same(run, result.Value.Run);
        Assert.Same(combat, result.Value.Combat);
        decisions.VerifyNoOtherCalls();
        commands.VerifyNoOtherCalls();
        flow.VerifyNoOtherCalls();
    }

    [Fact]
    public void Drive_StopsAtPriorityOwnedByPlayer()
    {
        var flow = new Mock<ICombatFlowPlanner>();
        var decisions = new Mock<IDecisionPolicyRegistry>();
        var commands = new Mock<ICombatCommandHandler>();
        var run = Run(maximumSteps: 10);
        var combat = Combat("enemy", waitingForInput: false) with
        {
            PriorityWindow = new PriorityWindowState
            {
                WindowId = "player-response",
                OpenedByActorId = "enemy",
                HolderActorId = "hero",
                EligibleActorIds = ["hero", "enemy"]
            }
        };
        var driver = new AutomaticFlowDriver(flow.Object, decisions.Object, commands.Object);

        var result = driver.Drive(new AutomaticFlowRequest
        {
            CombatId = combat.CombatId,
            Run = run,
            Combat = combat,
            RequestsActivationAdvance = true
        });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.Steps);
        Assert.Same(combat, result.Value.Combat);
        decisions.VerifyNoOtherCalls();
        commands.VerifyNoOtherCalls();
        flow.VerifyNoOtherCalls();
    }

    [Fact]
    public void Drive_WhenAutomaticStepLimitIsReached_ReturnsFailureWithoutPublishingSnapshots()
    {
        var flow = new Mock<ICombatFlowPlanner>();
        var decisions = new Mock<IDecisionPolicyRegistry>();
        var commands = new Mock<ICombatCommandHandler>();
        var run = Run(maximumSteps: 0);
        var combat = Combat("enemy", waitingForInput: false);
        var driver = new AutomaticFlowDriver(flow.Object, decisions.Object, commands.Object);

        var result = driver.Drive(new AutomaticFlowRequest
        {
            CombatId = combat.CombatId,
            Run = run,
            Combat = combat
        });

        Assert.True(result.IsFailure);
        Assert.Contains("exceeded 0 steps", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, run.Sequence);
        Assert.Equal("enemy", combat.ActivationState!.ActiveActorId);
        decisions.VerifyNoOtherCalls();
        commands.VerifyNoOtherCalls();
        flow.VerifyNoOtherCalls();
    }

    private static RunState Run(int maximumSteps) => new()
    {
        RunId = Guid.Parse("30000000-0000-0000-0000-000000000015"),
        Determinism = DeterministicContext.Create(15, "stage-15"),
        ResolvedMode = new ResolvedGameMode
        {
            CombatRules = new CombatRulesDefinition
            {
                Flow = new CombatFlowPoliciesDefinition
                {
                    AutomaticResolution = new AutomaticResolutionPolicyDefinition
                    {
                        Strategy = AutomaticResolutionStrategy.ToNextPlayerInput,
                        MaxAutomaticSteps = maximumSteps
                    },
                    Ai = new AiTurnPolicyDefinition
                    {
                        Enabled = true,
                        AutoEndAfterAction = true,
                        DecisionIds = ["test-ai"],
                        Intent = new IntentPolicyDefinition
                        {
                            Refresh = IntentRefreshStrategy.RecomputeOnPublish,
                            WhenInvalid = InvalidIntentStrategy.Recompute
                        }
                    }
                }
            }
        }
    };

    private static CombatState Combat(string activeActorId, bool waitingForInput) => new()
    {
        CombatId = Guid.Parse("40000000-0000-0000-0000-000000000015"),
        Actors = new[]
        {
            Actor("hero", ControllerKind.Player),
            Actor("enemy", ControllerKind.AI)
        }.ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
        ActivationState = new ActivationState
        {
            ActiveActorId = activeActorId,
            ActivationOrder = ["hero", "enemy"],
            WaitingForInput = waitingForInput
        },
        Determinism = DeterministicContext.Create(15, "stage-15")
    };

    private static CombatActorState Actor(string id, ControllerKind controller) => new()
    {
        InstanceId = id,
        DefinitionId = id,
        ContentRevision = "stage-15",
        Name = id,
        SideId = controller == ControllerKind.Player ? "players" : "enemies",
        ControllerBinding = new ControllerBinding
        {
            Kind = controller,
            PolicyId = controller == ControllerKind.AI ? "test-ai" : null
        }
    };
}
