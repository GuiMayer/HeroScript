using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Activation;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Determinism;
using Core.Entity.Controllers;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat;

public sealed class CombatRunCoordinatorTests
{
    private readonly Mock<ICombatSystem> _combatSystem = new();
    private readonly Mock<IRunManager> _runManager = new();
    private readonly Mock<IActionManager> _actionManager = new();
    private readonly Mock<IScriptModifierManager> _scriptModifierManager = new();

    public CombatRunCoordinatorTests()
    {
        _combatSystem.Setup(system => system.RestoreCombatState(It.IsAny<CombatState>()))
            .Returns((CombatState state) => Result<CombatState>.Success(state));
        _combatSystem.Setup(system => system.RemoveCombatState(It.IsAny<Guid>()))
            .Returns(Result.Success());
    }

    [Fact]
    public void StartEncounter_DerivesSeedAndOwnershipFromRun()
    {
        var run = CreateRunState(Guid.NewGuid(), hand: [], activeEncounter: false);
        var expectedSeed = run.Determinism.DrawUInt64().Value;
        var combat = CreateCombatState(run.RunId, run.CurrentNodeId!, expectedSeed);
        var attached = run with
        {
            ActiveEncounterId = combat.CombatId,
            Encounters = [new RunEncounterState { NodeId = "combat", Combat = combat }]
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        _combatSystem.Setup(system => system.StartCombat(
                "hero",
                It.Is<List<string>>(enemies => enemies.SequenceEqual(new[] { "enemy" })),
                3,
                It.Is<CombatStartOptions>(options =>
                    options.Seed == expectedSeed &&
                    options.ContentRevision == run.Determinism.ContentRevision &&
                    options.RunId == run.RunId &&
                    options.RunNodeId == "combat")))
            .Returns(Result<CombatState>.Success(combat));
        _runManager.Setup(manager => manager.AttachEncounter(
                run.RunId,
                run.Sequence,
                run.Determinism.Step,
                combat))
            .Returns(Result<RunState>.Success(attached));

        var result = CreateCoordinator().StartEncounter(run.RunId, "hero", ["enemy"]);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(combat.CombatId, result.Value.RunState.ActiveEncounterId);
        Assert.Equal(expectedSeed, result.Value.CombatState.Determinism.Seed);
    }

    [Fact]
    public void ExecuteAction_WithCard_CommitsCombatAndDiscardAtomically()
    {
        var run = CreateRunState(Guid.NewGuid(), ["fireball"]);
        var previous = run.GetActiveEncounter()!.Combat;
        var next = previous with { Determinism = previous.Determinism.AdvanceStep() };
        var command = Command(run.RunId, "fireball");
        var committed = run with
        {
            Deck = run.Deck with { Hand = [], DiscardPile = ["fireball"] },
            Encounters = [run.GetActiveEncounter()! with { Combat = next }]
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        _actionManager.Setup(manager => manager.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition { ActionId = "fireball" }));
        _combatSystem.Setup(system => system.ExecuteAction(previous.CombatId, It.IsAny<CombatActionCommand>()))
            .Returns(Result<CombatState>.Success(next));
        _runManager.Setup(manager => manager.CommitCombatAction(
                run.RunId,
                run.Sequence,
                previous,
                next,
                It.IsAny<CombatActionCommand>(),
                "fireball",
                CardConsumeDestination.Discard))
            .Returns(Result<RunState>.Success(committed));

        var result = CreateCoordinator().ExecuteAction(previous.CombatId, command);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("fireball", result.Value.ConsumedCardId);
        Assert.Equal(CardConsumeDestination.Discard, result.Value.Destination);
        Assert.Contains("fireball", result.Value.RunState.Deck.DiscardPile);
        _runManager.Verify(manager => manager.ConsumeCardsFromHand(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<CardConsumeDestination>()), Times.Never);
    }

    [Fact]
    public void ExecuteAction_WithExhaustAndModifiers_CommitsExactCommand()
    {
        var run = CreateRunState(Guid.NewGuid(), ["fireball"]);
        var previous = run.GetActiveEncounter()!.Combat;
        var next = previous with { Determinism = previous.Determinism.AdvanceStep() };
        var command = Command(run.RunId, "fireball");
        var definition = new ActionDefinition
        {
            ActionId = "fireball",
            Tags = ["spell", "exhaust"]
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        _actionManager.Setup(manager => manager.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(definition));
        _scriptModifierManager.Setup(manager => manager.GetPipelineModifiers(
                $"run:{run.RunId}",
                definition.Tags))
            .Returns(new Dictionary<string, float> { ["added_damage"] = 3 });
        _combatSystem.Setup(system => system.ExecuteAction(
                previous.CombatId,
                It.Is<CombatActionCommand>(actual => actual.RunModifiers["added_damage"] == 3)))
            .Returns(Result<CombatState>.Success(next));
        _runManager.Setup(manager => manager.CommitCombatAction(
                run.RunId,
                run.Sequence,
                previous,
                next,
                It.Is<CombatActionCommand>(actual => actual.RunModifiers["added_damage"] == 3),
                "fireball",
                CardConsumeDestination.Exhaust))
            .Returns(Result<RunState>.Success(run));

        var result = CreateCoordinator().ExecuteAction(previous.CombatId, command);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(CardConsumeDestination.Exhaust, result.Value.Destination);
    }

    [Fact]
    public void ExecuteAction_WhenPersistenceFails_RestoresAuthoritativeCombat()
    {
        var run = CreateRunState(Guid.NewGuid(), ["fireball"]);
        var previous = run.GetActiveEncounter()!.Combat;
        var next = previous with { Determinism = previous.Determinism.AdvanceStep() };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        _actionManager.Setup(manager => manager.GetDefinition("fireball"))
            .Returns(Result<ActionDefinition>.Success(new ActionDefinition { ActionId = "fireball" }));
        _combatSystem.Setup(system => system.ExecuteAction(previous.CombatId, It.IsAny<CombatActionCommand>()))
            .Returns(Result<CombatState>.Success(next));
        _runManager.Setup(manager => manager.CommitCombatAction(
                run.RunId,
                run.Sequence,
                previous,
                next,
                It.IsAny<CombatActionCommand>(),
                "fireball",
                CardConsumeDestination.Discard))
            .Returns(Result<RunState>.Failure("disk unavailable"));

        var result = CreateCoordinator().ExecuteAction(previous.CombatId, Command(run.RunId, "fireball"));

        Assert.True(result.IsFailure);
        Assert.Contains("disk unavailable", result.Error);
        _combatSystem.Verify(system => system.RestoreCombatState(previous), Times.Exactly(2));
    }

    [Fact]
    public void ExecuteAction_WhenCardMissing_DoesNotExecuteCombat()
    {
        var run = CreateRunState(Guid.NewGuid(), []);
        var combatId = run.ActiveEncounterId!.Value;
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));

        var result = CreateCoordinator().ExecuteAction(combatId, Command(run.RunId, "fireball"));

        Assert.True(result.IsFailure);
        Assert.Contains("not in run hand", result.Error);
        _combatSystem.Verify(system => system.ExecuteAction(
            It.IsAny<Guid>(),
            It.IsAny<CombatActionCommand>()), Times.Never);
    }

    [Fact]
    public void ExecuteAction_PassStillPersistsCombatInsideRun()
    {
        var run = CreateRunState(Guid.NewGuid(), []);
        var previous = run.GetActiveEncounter()!.Combat;
        var next = previous with { Determinism = previous.Determinism.AdvanceStep() };
        var command = new CombatActionCommand
        {
            RunId = run.RunId,
            ActorId = "hero",
            ActionType = ActionType.PASS
        };
        _runManager.Setup(manager => manager.GetRun(run.RunId))
            .Returns(Result<RunState>.Success(run));
        _combatSystem.Setup(system => system.ExecuteAction(previous.CombatId, command))
            .Returns(Result<CombatState>.Success(next));
        _runManager.Setup(manager => manager.CommitCombatAction(
                run.RunId,
                run.Sequence,
                previous,
                next,
                command,
                null,
                CardConsumeDestination.None))
            .Returns(Result<RunState>.Success(run));

        var result = CreateCoordinator().ExecuteAction(previous.CombatId, command);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(CardConsumeDestination.None, result.Value.Destination);
    }

    [Fact]
    public void EndTurn_CanonicalFlowResolvesEnemyAndCommitsOneBatch()
    {
        var flowPlanner = new Mock<ICombatFlowPlanner>();
        var gambits = new Mock<IGambitEngine>();
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
            .Returns(Result<CombatState>.Success(attackedState))
            .Returns(Result<CombatState>.Success(endedState));
        flowPlanner.SetupSequence(planner => planner.AdvanceActivation(
                run,
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
            _actionManager.Object,
            _scriptModifierManager.Object,
            flowPlanner.Object,
            gambits.Object);
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
        new(_combatSystem.Object, _runManager.Object, _actionManager.Object, _scriptModifierManager.Object);

    private static CombatActionCommand Command(Guid runId, string actionId) => new()
    {
        RunId = runId,
        CardId = actionId,
        ActorId = "hero",
        ActionType = ActionType.POWER,
        PowerId = actionId,
        TargetId = "enemy"
    };

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
        ResourceState = new EntityResourceState
        {
            EntityId = id,
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
            Deck = new DeckState { Hand = hand },
            Determinism = context
        };
    }
}
