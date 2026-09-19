using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Activation;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public sealed class CombatRunCoordinatorTests
{
    private readonly Mock<ICombatFactory> _combatFactory = new();
    private readonly Mock<IRunEncounterRuntime> _runManager = new();

    [Fact]
    public void ExecuteAction_ComposesHandlerAndAutomaticDriverAndCommitsOneBatch()
    {
        var flowPlanner = new Mock<ICombatFlowPlanner>();
        var commands = new Mock<ICombatCommandHandler>();
        var automaticFlow = new Mock<IAutomaticFlowDriver>();
        var committer = _runManager.As<IRunCombatResolutionCommitter>();
        var run = CreateRunState(Guid.NewGuid(), []) with
        {
            ResolvedMode = new ResolvedGameMode
            {
                CombatRules = new CombatRulesDefinition { Flow = CanonicalPolicies() }
            }
        };
        var previous = WithActivation(run.GetActiveEncounter()!.Combat, "hero", waiting: true);
        run = run with
        {
            Encounters = [run.GetActiveEncounter()! with { Combat = previous }]
        };
        var rootState = previous with { Determinism = previous.Determinism.AdvanceStep() };
        var enemyState = WithActivation(rootState, "enemy", waiting: false);
        var attackedState = enemyState with { Determinism = enemyState.Determinism.AdvanceStep() };
        var endedState = attackedState with { Determinism = attackedState.Determinism.AdvanceStep() };
        var playerState = WithActivation(endedState, "hero", waiting: true);
        var command = new CombatActionCommand
        {
            RunId = run.RunId,
            ActorId = "hero",
            ActionType = ActionType.END_TURN
        };
        var identity = new RunCommandIdentity(
            Guid.NewGuid(), "END_TURN", run.Sequence, previous.Determinism.Step, "hash");

        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        var rootCandidate = new LegalActionCandidate
        {
            CandidateId = "end-turn",
            Source = LegalActionSource.System,
            Command = command,
            ResolvedCommand = command,
            SuccessorCombat = rootState,
            SuccessorRun = run,
            ResolutionFingerprint = "resolution"
        };
        var rootStep = new CombatResolutionStep
        {
            TransitionType = "combat.action.applied",
            Combat = rootState,
            Deck = run.Deck,
            RunDeterminism = run.Determinism,
            RunSnapshot = run
        };
        commands.Setup(handler => handler.Handle(It.Is<CombatCommandHandlingRequest>(request =>
                request.CombatId == previous.CombatId && request.Origin == CombatCommandOrigin.PlayerInput)))
            .Returns(Result<CombatCommandHandlingResult>.Success(new CombatCommandHandlingResult
            {
                Step = rootStep,
                NextRun = run with { Determinism = run.Determinism.AdvanceStep() },
                Candidate = rootCandidate,
                RequestsActivationAdvance = true
            }));
        var automaticSteps = new[]
        {
            Step("combat.activation.ended", enemyState, run.Deck),
            Step("combat.activation.started", enemyState, run.Deck),
            Step("combat.ai.action", attackedState, run.Deck),
            Step("combat.ai.end_turn", endedState, run.Deck),
            Step("combat.activation.ended", playerState, run.Deck),
            Step("combat.activation.started", playerState, run.Deck)
        };
        automaticFlow.Setup(driver => driver.Drive(It.Is<AutomaticFlowRequest>(request =>
                request.RequestsActivationAdvance && request.Combat == rootState)))
            .Returns(Result<AutomaticFlowResult>.Success(new AutomaticFlowResult
            {
                Run = run,
                Combat = playerState,
                Steps = automaticSteps
            }));
        CombatResolutionCommit? captured = null;
        committer.Setup(service => service.CommitCombatResolution(It.IsAny<CombatResolutionCommit>()))
            .Returns((CombatResolutionCommit resolution) =>
            {
                captured = resolution;
                return Result<RunState>.Success(run with
                {
                    Sequence = run.Sequence + resolution.Steps.Count,
                    Encounters = [run.GetActiveEncounter()! with { Combat = resolution.Steps[^1].Combat }]
                });
            });

        var coordinator = new CombatRunCoordinator(
            _combatFactory.Object,
            _runManager.Object,
            flowPlanner.Object,
            commands.Object,
            automaticFlow.Object);
        var result = coordinator.ExecuteAction(previous.CombatId, command, identity);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("hero", result.Value.CombatState.ActivationState!.ActiveActorId);
        Assert.True(result.Value.CombatState.ActivationState.WaitingForInput);
        Assert.NotNull(captured);
        Assert.Equal(7, captured!.Steps.Count);
        Assert.Equal("combat.action.applied", captured.Steps[0].TransitionType);
        Assert.Contains(captured.Steps, step => step.TransitionType == "combat.ai.action");
        Assert.Contains(captured.Steps, step => step.TransitionType == "combat.ai.end_turn");
        committer.Verify(service => service.CommitCombatResolution(It.IsAny<CombatResolutionCommit>()), Times.Once);
    }

    [Fact]
    public void ExecuteAction_WhenAutomaticContinuationFails_DoesNotCommitTheRootStep()
    {
        var commands = new Mock<ICombatCommandHandler>();
        var automaticFlow = new Mock<IAutomaticFlowDriver>();
        var committer = _runManager.As<IRunCombatResolutionCommitter>();
        var run = CreateRunState(Guid.NewGuid(), []) with
        {
            ResolvedMode = new ResolvedGameMode
            {
                CombatRules = new CombatRulesDefinition { Flow = CanonicalPolicies() }
            }
        };
        var previous = WithActivation(run.GetActiveEncounter()!.Combat, "hero", waiting: true);
        run = run with { Encounters = [run.GetActiveEncounter()! with { Combat = previous }] };
        var candidateState = previous with { CurrentTurn = previous.CurrentTurn + 1 };
        var command = new CombatActionCommand
        {
            RunId = run.RunId,
            ActorId = "hero",
            ActionType = ActionType.END_TURN
        };
        var candidate = new LegalActionCandidate
        {
            CandidateId = "end-turn",
            Source = LegalActionSource.System,
            Command = command,
            ResolvedCommand = command,
            SuccessorCombat = candidateState,
            SuccessorRun = run,
            ResolutionFingerprint = "candidate"
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        commands.Setup(handler => handler.Handle(It.IsAny<CombatCommandHandlingRequest>()))
            .Returns(Result<CombatCommandHandlingResult>.Success(new CombatCommandHandlingResult
            {
                Step = Step("combat.action.applied", candidateState, run.Deck),
                NextRun = run with { Determinism = run.Determinism.AdvanceStep() },
                Candidate = candidate,
                RequestsActivationAdvance = true
            }));
        automaticFlow.Setup(driver => driver.Drive(It.IsAny<AutomaticFlowRequest>()))
            .Returns(Result<AutomaticFlowResult>.Failure("automatic continuation rejected"));
        var coordinator = new CombatRunCoordinator(
            _combatFactory.Object,
            _runManager.Object,
            Mock.Of<ICombatFlowPlanner>(),
            commands.Object,
            automaticFlow.Object);

        var result = coordinator.ExecuteAction(previous.CombatId, command);

        Assert.True(result.IsFailure);
        Assert.Equal("automatic continuation rejected", result.Error);
        Assert.Equal(1, previous.CurrentTurn);
        Assert.Equal(1, run.GetActiveEncounter()!.Combat.CurrentTurn);
        committer.Verify(service => service.CommitCombatResolution(It.IsAny<CombatResolutionCommit>()), Times.Never);
    }

    [Fact]
    public void GetAndResolveEncounter_UseRunSnapshotAsAuthority()
    {
        var run = CreateRunState(Guid.NewGuid(), []);
        var encounter = run.GetActiveEncounter()!;
        var terminalCombat = encounter.Combat with { Status = CombatStatus.VICTORY };
        run = run with { Encounters = [encounter with { Combat = terminalCombat }] };
        var resolvedRun = run with
        {
            ActiveEncounterId = null,
            Encounters = [encounter with { Combat = terminalCombat, Resolved = true, Outcome = "VICTORY" }]
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        _runManager.Setup(manager => manager.GetRunByCombat(terminalCombat.CombatId))
            .Returns(Result<RunState>.Success(run));
        _runManager.Setup(manager => manager.ResolveEncounter(
                run.RunId,
                run.Sequence,
                terminalCombat.CombatId))
            .Returns(Result<RunState>.Success(resolvedRun));
        var coordinator = CreateCoordinator();

        var recovered = coordinator.GetCombatState(terminalCombat.CombatId);
        var resolved = coordinator.ResolveEncounter(run.RunId, terminalCombat.CombatId);

        Assert.True(recovered.IsSuccess, recovered.IsFailure ? recovered.Error : null);
        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : null);
        Assert.Null(resolved.Value.RunState.ActiveEncounterId);
    }

    private CombatRunCoordinator CreateCoordinator() =>
        new(
            _combatFactory.Object,
            _runManager.Object,
            Mock.Of<ICombatFlowPlanner>(),
            Mock.Of<ICombatCommandHandler>(),
            Mock.Of<IAutomaticFlowDriver>());

    private static CombatResolutionStep Step(string type, CombatState combat, DeckState deck) => new()
    {
        TransitionType = type,
        Combat = combat,
        Deck = deck
    };

    private static CombatState CreateCombatState(Guid runId, string nodeId, ulong seed) => new()
    {
        CombatId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
        RunId = runId,
        RunNodeId = nodeId,
        Actors = new[] { CreateEntity("hero", true), CreateEntity("enemy", false) }
            .ToDictionary(actor => actor.InstanceId, StringComparer.Ordinal),
        Determinism = DeterministicContext.Create(seed, new string('c', 64))
    };

    private static CombatState WithActivation(CombatState combat, string actorId, bool waiting)
    {
        return combat with
        {
            ActivationState = new ActivationState
            {
                ActiveActorId = actorId,
                ActivationOrder = ["hero", "enemy"],
                WaitingForInput = waiting
            },
            PhaseState = new PhaseState
            {
                SequenceId = "test",
                ContentRevision = combat.Determinism.ContentRevision,
                Cursor = "action"
            }
        };
    }

    private static CombatFlowPoliciesDefinition CanonicalPolicies() => new()
    {
        AutomaticResolution = new()
        {
            Strategy = AutomaticResolutionStrategy.ToNextPlayerInput,
            MaxAutomaticSteps = 20
        },
        ActionBudget = new()
        {
            Strategy = ActionBudgetStrategy.FixedCount,
            ActionCosts = ActionCostStrategy.Configured,
            ActorScope = FlowActorScope.PlayerControlled,
            MaxActionsPerActivation = 2,
            ConsumingCommands = ["EXECUTE_ACTION"]
        },
        Ai = new()
        {
            Enabled = true,
            AutoEndAfterAction = true,
            DecisionIds = ["enemy_basic_attack"],
            Intent = new()
            {
                Refresh = IntentRefreshStrategy.RecomputeOnPublish,
                WhenInvalid = InvalidIntentStrategy.Recompute
            }
        },
        Outcome = new()
        {
            EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction,
            TieBreak = OutcomeTieBreak.Draw
        }
    };

    private static CombatActorState CreateEntity(string id, bool isHero) => new()
    {
        InstanceId = id,
        Name = id,
        SideId = isHero ? "player" : "opposition", ControllerBinding = new ControllerBinding
        {
            Kind = isHero ? ControllerKind.Player : ControllerKind.AI,
            PolicyId = isHero ? null : "gambit"
        },
        ResourceState = new ResourceSet
        {
            OwnerId = id,
            Resources = new Dictionary<string, ResourcePool>
            {
                ["health"] = new()
                {
                    ResourceId = "health",
                    Current = 100,
                    Maximum = 100,
                    Minimum = 0,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = "health",
                        DisplayName = "Health",
                        Category = ResourceCategory.VITAL
                    }
                }
            }
        }
    };

    private static RunState CreateRunState(
        Guid runId,
        IReadOnlyList<string> hand,
        bool activeEncounter = true)
    {
        var map = RunMapTransitions.Create(
        [
            new RunMapNodeDefinition
            {
                NodeId = "combat",
                Activity = new RunActivityDefinition { Type = RunActivityType.Encounter }
            }
        ]).Value;
        var context = DeterministicContext.Create(44, new string('c', 64));
        var combat = CreateCombatState(runId, "combat", context.DrawUInt64().Value);
        return new RunState
        {
            RunId = runId,
            Sequence = 7,
            CurrentNodeId = "combat",
            Map = map,
            ActiveEncounterId = activeEncounter ? combat.CombatId : null,
            Encounters = activeEncounter
                ? [new RunEncounterState { NodeId = "combat", Combat = combat }]
                : [],
            Deck = CreateHand(hand, context),
            Determinism = context
        };
    }

    private static DeckState CreateHand(
        IReadOnlyList<string> definitionIds,
        DeterministicContext context)
    {
        var created = DeckTransitions.Create(definitionIds, context);
        if (created.IsFailure)
            throw new InvalidOperationException(created.Error);
        return created.Value.State with
        {
            DrawPileInstanceIds = [],
            HandInstanceIds = created.Value.State.DrawPileInstanceIds
        };
    }
}
