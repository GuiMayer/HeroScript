using Core.Combat.Activation;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.GameplayEvents;
using Core.Combat.Modifiers;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatCommandHandlerTests
{
    [Theory]
    [InlineData("hero", CombatCommandOrigin.PlayerInput, true)]
    [InlineData("enemy", CombatCommandOrigin.AutomaticController, false)]
    public void ObservedHandlerOwnsTheActionRootAndActualCommandModifierTick(string actorId, CombatCommandOrigin origin, bool waiting)
    {
        var run = Run() with { Modifiers = [new()
        {
            InstanceId = DeterministicId.Create(15, 2, "timer"), ModifierId = "timer", Duration = 2, Stacks = 1,
            Owner = new() { Kind = GameplayOwnerKind.Entity, Id = actorId }, ContentRevision = "stage-15",
            Definition = new() { ModifierId = "timer", DurationBoundary = ModifierDurationBoundary.Command }
        }] };
        var combat = Combat(actorId, waiting);
        LegalActionEvaluation Evaluation(RunState sourceRun, CombatState sourceCombat, CombatActionCommand command) => new()
        {
            Candidate = new() { CandidateId = "candidate", Source = LegalActionSource.System, Command = command, ResolvedCommand = command,
                SuccessorRun = sourceRun, SuccessorCombat = sourceCombat, ResolutionFingerprint = "resolution" }
        };
        var legal = new Mock<ILegalActionResolver>();
        legal.Setup(service => service.Evaluate(It.IsAny<RunState>(), It.IsAny<CombatState>(), It.IsAny<CombatActionCommand>(), It.IsAny<CombatCommandOrigin>()))
            .Returns((RunState state, CombatState current, CombatActionCommand command, CombatCommandOrigin _) => Result<LegalActionEvaluation>.Success(Evaluation(state, current, command)));
        legal.Setup(service => service.EvaluateObserved(It.IsAny<RunState>(), It.IsAny<CombatState>(), It.IsAny<CombatActionCommand>(), It.IsAny<CombatCommandOrigin>(), It.IsAny<GameplayEffectObservation>()))
            .Returns((RunState state, CombatState current, CombatActionCommand command, CombatCommandOrigin _, GameplayEffectObservation context) =>
                Result<ObservedGameplayExecution<LegalActionEvaluation>>.Success(new(Evaluation(state, current, command), context)));
        var handler = new CombatCommandHandler(legal.Object, new CombatActionStateReducer(new CombatOutcomeResolver()));
        var request = Request(run, combat, actorId, origin);
        var input = CanonicalJson.ComputeHash(request);
        var observation = Core.Tests.GameplayEvents.GameplayObservedExecutorTests.Observation(revision: "stage-15");
        var baseline = handler.Handle(request);
        Assert.True(baseline.IsSuccess, baseline.IsFailure ? baseline.Error : null);
        var hashes = new HashSet<string>();
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var actual = handler.HandleObserved(request, observation);
            Assert.True(actual.IsSuccess, actual.IsFailure ? actual.Error : null);
            Assert.Equal(CanonicalJson.ComputeHash(baseline.Value), CanonicalJson.ComputeHash(actual.Value.Execution));
            var facts = actual.Value.Observation.Events.Occurrences;
            Assert.Equal(new[] { "action.started", "modifier.duration.changed", "action.completed" }, facts.Select(item => item.Kind));
            Assert.Equal(facts[0].EventId, facts[1].ParentEventId);
            Assert.Equal(facts[0].EventId, facts[2].ParentEventId);
            Assert.Equal(new[] { facts[1].EventId }, Assert.IsType<ActionOccurrencePayload>(facts[2].Payload).ChildEventIds);
            Assert.Null(actual.Value.Observation.ParentEventId);
            hashes.Add(CanonicalJson.ComputeHash(facts));
        }
        Assert.Single(hashes);
        var constrained = Core.Tests.GameplayEvents.GameplayObservedExecutorTests.Observation(2, "stage-15");
        Assert.True(handler.HandleObserved(request, constrained).IsFailure);
        Assert.Empty(constrained.Events.Occurrences);
        Assert.Empty(observation.Events.Occurrences);
        Assert.Equal(input, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void Handle_UsesTheSameLegalAndReductionPipelineForPlayerAndAutomaticCommands()
    {
        var legal = new Mock<ILegalActionResolver>();
        var reducer = new Mock<ICombatActionStateReducer>();
        var run = Run();
        var playerCombat = Combat("hero", waitingForInput: true);
        var automaticCombat = Combat("enemy", waitingForInput: false);
        legal.Setup(service => service.Evaluate(
                It.IsAny<RunState>(),
                It.IsAny<CombatState>(),
                It.IsAny<CombatActionCommand>(),
                It.IsAny<CombatCommandOrigin>()))
            .Returns((RunState currentRun, CombatState currentCombat, CombatActionCommand command,
                CombatCommandOrigin _) => Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
                {
                    Candidate = new LegalActionCandidate
                    {
                        CandidateId = $"candidate:{command.ActorId}",
                        Source = LegalActionSource.System,
                        Command = command,
                        ResolvedCommand = command,
                        SuccessorRun = currentRun,
                        SuccessorCombat = currentCombat,
                        ResolutionFingerprint = $"resolution:{command.ActorId}"
                    }
                }));
        reducer.Setup(service => service.Apply(
                It.IsAny<RunState>(),
                It.IsAny<CombatState>(),
                It.IsAny<LegalActionCandidate>(),
                It.IsAny<CombatActionCommand>(),
                It.IsAny<CombatFlowPoliciesDefinition>()))
            .Returns((RunState currentRun, CombatState currentCombat, LegalActionCandidate candidate,
                CombatActionCommand _, CombatFlowPoliciesDefinition _) =>
                Result<CombatActionReduction>.Success(new CombatActionReduction
                {
                    Run = currentRun,
                    Combat = currentCombat,
                    ResolvedCommand = candidate.ResolvedCommand
                }));
        var handler = new CombatCommandHandler(legal.Object, reducer.Object);

        var player = handler.Handle(Request(run, playerCombat, "hero", CombatCommandOrigin.PlayerInput));
        var automatic = handler.Handle(Request(
            run,
            automaticCombat,
            "enemy",
            CombatCommandOrigin.AutomaticController));

        Assert.True(player.IsSuccess, player.IsFailure ? player.Error : null);
        Assert.True(automatic.IsSuccess, automatic.IsFailure ? automatic.Error : null);
        legal.Verify(service => service.Evaluate(
            run, playerCombat, It.IsAny<CombatActionCommand>(), CombatCommandOrigin.PlayerInput), Times.Once);
        legal.Verify(service => service.Evaluate(
            run, automaticCombat, It.IsAny<CombatActionCommand>(), CombatCommandOrigin.AutomaticController), Times.Once);
        reducer.Verify(service => service.Apply(
            It.IsAny<RunState>(),
            It.IsAny<CombatState>(),
            It.IsAny<LegalActionCandidate>(),
            It.IsAny<CombatActionCommand>(),
            It.IsAny<CombatFlowPoliciesDefinition>()), Times.Exactly(2));
    }

    [Fact]
    public void Handle_WhenReductionFails_DoesNotExposeAPartialSuccessor()
    {
        var legal = new Mock<ILegalActionResolver>();
        var reducer = new Mock<ICombatActionStateReducer>();
        var run = Run();
        var combat = Combat("hero", waitingForInput: true);
        var command = Command(run.RunId, "hero");
        legal.Setup(service => service.Evaluate(run, combat, command, CombatCommandOrigin.PlayerInput))
            .Returns(Result<LegalActionEvaluation>.Success(new LegalActionEvaluation
            {
                Candidate = new LegalActionCandidate
                {
                    CandidateId = "candidate",
                    Command = command,
                    SuccessorRun = run with { Sequence = 999 },
                    SuccessorCombat = combat with { CurrentTurn = 999 }
                }
            }));
        reducer.Setup(service => service.Apply(
                run,
                combat,
                It.IsAny<LegalActionCandidate>(),
                command,
                It.IsAny<CombatFlowPoliciesDefinition>()))
            .Returns(Result<CombatActionReduction>.Failure("reduction rejected"));
        var handler = new CombatCommandHandler(legal.Object, reducer.Object);

        var result = handler.Handle(new CombatCommandHandlingRequest
        {
            CombatId = combat.CombatId,
            Run = run,
            Combat = combat,
            Command = command,
            Origin = CombatCommandOrigin.PlayerInput
        });

        Assert.True(result.IsFailure);
        Assert.Equal("reduction rejected", result.Error);
        Assert.Equal(0, run.Sequence);
        Assert.Equal(1, combat.CurrentTurn);
    }

    private static CombatCommandHandlingRequest Request(
        RunState run,
        CombatState combat,
        string actorId,
        CombatCommandOrigin origin) => new()
        {
            CombatId = combat.CombatId,
            Run = run,
            Combat = combat,
            Command = Command(run.RunId, actorId),
            Origin = origin
        };

    private static CombatActionCommand Command(Guid runId, string actorId) => new()
    {
        RunId = runId,
        ActorId = actorId,
        ActionType = ActionType.END_TURN
    };

    private static RunState Run() => new()
    {
        RunId = Guid.Parse("10000000-0000-0000-0000-000000000015"),
        Determinism = DeterministicContext.Create(15, "stage-15"),
        ResolvedMode = new ResolvedGameMode
        {
            CombatRules = new CombatRulesDefinition
            {
                Flow = new CombatFlowPoliciesDefinition
                {
                    ActionBudget = new ActionBudgetPolicyDefinition
                    {
                        Strategy = ActionBudgetStrategy.FixedCount,
                        ActionCosts = ActionCostStrategy.Configured,
                        ActorScope = FlowActorScope.All,
                        MaxActionsPerActivation = 2
                    },
                    Outcome = new OutcomePolicyDefinition
                    {
                        EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction,
                        TieBreak = OutcomeTieBreak.Draw
                    }
                }
            }
        }
    };

    private static CombatState Combat(string activeActorId, bool waitingForInput) => new()
    {
        CombatId = Guid.Parse("20000000-0000-0000-0000-000000000015"),
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
        PhaseState = new PhaseState
        {
            SequenceId = "test",
            ContentRevision = "stage-15",
            Cursor = "action"
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
