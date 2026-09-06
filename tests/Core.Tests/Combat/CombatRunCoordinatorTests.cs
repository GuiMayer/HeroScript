using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Activation;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Determinism;
using Core.Entity.Controllers;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public sealed class CombatRunCoordinatorTests
{
    private readonly Mock<ICombatSystem> _combatSystem = new();
    private readonly Mock<IRunManager> _runManager = new();
    private readonly Mock<ICardPlayExecutor> _cardPlayExecutor = new();

    public CombatRunCoordinatorTests()
    {
        _combatSystem.Setup(system => system.RestoreCombatState(It.IsAny<CombatState>()))
            .Returns((CombatState state) => Result<CombatState>.Success(state));
        _combatSystem.Setup(system => system.RemoveCombatState(It.IsAny<Guid>()))
            .Returns(Result.Success());
    }

    [Fact]
    public void EndTurn_CanonicalFlowResolvesEnemyAndCommitsOneBatch()
    {
        var flowPlanner = new Mock<ICombatFlowPlanner>();
        var gambits = new Mock<IGambitEngine>();
        var abilities = new Mock<IAbilityExecutor>();
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
        _combatSystem.SetupSequence(system => system.ExecuteAction(
                previous.CombatId,
                It.IsAny<CombatActionCommand>()))
            .Returns(Result<CombatState>.Success(rootState))
            .Returns(Result<CombatState>.Success(endedState));
        abilities.Setup(executor => executor.Execute(It.IsAny<AbilityExecutionRequest>()))
            .Returns(Result<AbilityExecutionResult>.Success(new AbilityExecutionResult
            {
                Combat = attackedState,
                Definition = new ActionDefinition
                {
                    ActionId = "basic_attack",
                    ActionType = ActionType.BASIC_ATTACK
                },
                Evaluation = new CardPlayEvaluation
                {
                    IsLegal = true,
                    ActorId = "enemy",
                    ResolvedTargetIds = ["hero"]
                },
                ResolutionFingerprint = "ability-resolution"
            }));
        flowPlanner.SetupSequence(planner => planner.AdvanceActivation(
                It.Is<RunState>(snapshot => snapshot.RunId == run.RunId),
                It.IsAny<CombatState>(),
                It.IsAny<DeckState>(),
                It.IsAny<DeterministicContext>()))
            .Returns(Result<CombatFlowAdvanceResult>.Success(Plan(enemyState, run.Deck)))
            .Returns(Result<CombatFlowAdvanceResult>.Success(Plan(playerState, run.Deck)));
        gambits.Setup(engine => engine.DecideActionWithMetadata(
                It.IsAny<Core.Entity.Entity>(),
                enemyState,
                It.IsAny<IEnumerable<string>>()))
            .Returns(Result<GambitDecision>.Success(new GambitDecision
            {
                GambitId = "enemy_basic_attack",
                Action = new EntityAction
                {
                    ActionType = ActionType.BASIC_ATTACK,
                    TargetId = "hero"
                }
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
            _combatSystem.Object,
            _runManager.Object,
            _cardPlayExecutor.Object,
            flowPlanner.Object,
            gambits.Object,
            abilityExecutor: abilities.Object);
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
        _runManager.Verify(manager => manager.CommitCombatAction(
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<CombatState>(),
            It.IsAny<CombatState>(),
            It.IsAny<CombatActionCommand>(),
            It.IsAny<string?>(),
            It.IsAny<CardConsumeDestination>(),
            It.IsAny<RunCommandIdentity?>()), Times.Never);
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
        _combatSystem.Verify(system => system.RestoreCombatState(terminalCombat), Times.Once);
        _combatSystem.Verify(system => system.RemoveCombatState(terminalCombat.CombatId), Times.Once);
    }

    private CombatRunCoordinator CreateCoordinator() =>
        new(_combatSystem.Object, _runManager.Object, _cardPlayExecutor.Object);

    private static CombatState CreateCombatState(Guid runId, string nodeId, ulong seed) => new()
    {
        CombatId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
        RunId = runId,
        RunNodeId = nodeId,
        Hero = CreateEntity("hero", true),
        Enemies = [CreateEntity("enemy", false)],
        Determinism = DeterministicContext.Create(seed, new string('c', 64))
    };

    private static CombatState WithActivation(CombatState combat, string actorId, bool waiting)
    {
        var sequence = new PhaseSequenceDefinition
        {
            SequenceId = "test",
            Phases =
            [
                new PhaseDefinition { PhaseId = "start", Role = PhaseRole.Start, Order = 10 },
                new PhaseDefinition
                {
                    PhaseId = "action",
                    Role = PhaseRole.Middle,
                    Order = 20,
                    AllowedActions = [ActionType.BASIC_ATTACK, ActionType.POWER, ActionType.PASS, ActionType.END_TURN]
                },
                new PhaseDefinition { PhaseId = "end", Role = PhaseRole.End, Order = 30 }
            ]
        };
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
                CurrentPhaseId = "action",
                PhaseSequence = sequence,
                ActivePlayerId = actorId
            }
        };
    }

    private static CombatFlowAdvanceResult Plan(CombatState final, DeckState deck) => new()
    {
        Steps =
        [
            new CombatResolutionStep
            {
                TransitionType = "combat.activation.ended",
                Combat = final,
                Deck = deck
            },
            new CombatResolutionStep
            {
                TransitionType = "combat.activation.started",
                Combat = final,
                Deck = deck
            }
        ]
    };

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
            ActorScope = FlowActorScope.Player,
            MaxActionsPerActivation = 2,
            ConsumingCommands = ["EXECUTE_ACTION"]
        },
        Ai = new()
        {
            Enabled = true,
            AutoEndAfterAction = true,
            GambitIds = ["enemy_basic_attack"]
        },
        Outcome = new()
        {
            EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction,
            TieBreak = OutcomeTieBreak.Draw
        }
    };

    private static CombatEntity CreateEntity(string id, bool isHero) => new()
    {
        EntityId = id,
        Name = id,
        IsHero = isHero,
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
            new RunMapNodeDefinition { NodeId = "combat", NodeType = "combat" }
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
