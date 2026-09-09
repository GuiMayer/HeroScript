using Core.Combat;
using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Determinism;
using Core.Resources;
using Core.Run;
using Xunit;
using System.Collections.Immutable;
using System.Text.Json;
using Core.Content;
using Core.Effects;
using Core.Math;
using Core.Combat.Intents;
using Core.Combat.TurnOrder;
using Moq;

namespace Core.Tests.Combat.Flow;

public sealed class CombatFlowPlannerTests
{
    [Fact]
    public void LifecycleDeckChangesSurviveInitializationAndActivationPlanning()
    {
        const string path = "phase-sequences/test.json";
        var runtime = ContentRuntime.Create(new()
        {
            Manifest = new() { Revision = "revision", ConfigName = "default", Artifacts = [new()
                { Kind = "phase-sequences", Path = path, DefinitionCount = 1 }] },
            Artifacts = ImmutableDictionary<string, JsonElement>.Empty.Add(path,
                JsonSerializer.SerializeToElement(new Dictionary<string, PhaseSequenceDefinition> { ["test"] = Sequence() }))
        }).Value;
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(item => item.Resolve("revision", "default")).Returns(Result<ContentRuntime>.Success(runtime));
        var actions = new Mock<IActionManager>();
        actions.Setup(item => item.GetDefinition(It.IsAny<string>())).Returns((string id) => ResolveAction(id));
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var triggers = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor());
        var boundaries = new CombatBoundaryExecutor(TurnOrders(), actions.Object,
            new CombatStatusLifecycle(triggers), new CombatRelicLifecycle(triggers),
            new CombatResourceLifecycle(triggers), new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver());
        var planner = new CombatFlowPlanner(runtimes.Object, boundaries, Mock.Of<IIntentResolver>());
        var deck = DeckTransitions.Create(["strike", "strike", "strike"], DeterministicContext.Create(99, "revision")).Value;
        var policies = Policies() with { Ai = new() { PublishIntents = false }, DeckCycle = Policies().DeckCycle with
            { InitialHandSize = 0, EncounterStart = EncounterDeckStartStrategy.ResetOrdered,
                DrawPerActivation = 0, EndDiscard = DeckEndDiscardStrategy.None } };
        var run = new RunState
        {
            RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), PlayerEntityId = "hero", Deck = deck.State,
            Determinism = deck.Context,
            ResolvedMode = new() { CombatRules = new()
                { DefaultPhaseSequenceId = "test", TurnOrder = TurnPolicy(), Flow = policies } },
            Relics = [new()
            {
                RelicInstanceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), DefinitionId = "draw-on-boundary",
                Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, ContentRevision = "revision",
                Triggers = new[] { CombatTriggerBoundaries.CombatStart, "EndActivation" }.Select(boundary => new EffectTriggerDefinition
                {
                    TriggerId = boundary, Boundary = boundary,
                    Effects = [new() { Type = EffectType.DRAW_CARD, Target = EffectTarget.SELF, CardCount = 1 }]
                }).ToImmutableArray()
            }]
        };
        var combat = CombatTransitions.Create([Entity("hero", true, 0), Entity("enemy", false, 0)],
            DeterministicContext.Create(42, "revision"));
        var initialized = planner.InitializeTransaction(run, combat);
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Single(initialized.Value.Run.Deck.HandInstanceIds);
        Assert.NotEmpty(initialized.Value.EffectSteps);
        Assert.NotEmpty(initialized.Value.Applications);
        Assert.Equal(64, initialized.Value.Fingerprint.Length);
        Assert.Equal(
            Enumerable.Range(0, initialized.Value.EffectSteps.Count),
            initialized.Value.EffectSteps.Select(step => step.Index));
        var advanced = planner.AdvanceActivation(initialized.Value.Run, initialized.Value.Combat,
            initialized.Value.Run.Deck, initialized.Value.Run.Determinism);
        Assert.True(advanced.IsSuccess, advanced.IsFailure ? advanced.Error : null);
        Assert.Equal(2, advanced.Value.Deck.HandInstanceIds.Count);
        Assert.Equal(2, advanced.Value.Steps.Last(step => step.RunSnapshot != null).RunSnapshot!.Deck.HandInstanceIds.Count);
        Assert.Empty(run.Deck.HandInstanceIds);
        Assert.Equal("enemy", advanced.Value.Combat.ActivationState!.ActiveActorId);
    }

    [Fact]
    public void InitializeAndAdvance_UsePinnedPoliciesWithoutMutatingInputs()
    {
        var context = DeterministicContext.Create(42UL, "revision");
        var combat = CombatTransitions.Create(
            [Entity("hero", isHero: true, energy: 0), Entity("enemy", isHero: false, energy: 0)],
            context);
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            PlayerEntityId = "hero",
            Deck = new DeckState { Hand = ["strike", "retain"] },
            Determinism = DeterministicContext.Create(99UL, "revision")
        };
        var sequence = Sequence();
        var policies = Policies();

        var boundaries = Boundaries();
        var initialized = boundaries.InitializeTransaction(
            run, combat, sequence, policies, TurnPolicy());
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Equal("hero", initialized.Value.Combat.ActivationState!.ActiveActorId);
        Assert.True(initialized.Value.Combat.ActivationState.WaitingForInput);
        Assert.Equal("action", initialized.Value.Combat.PhaseState!.Cursor);
        Assert.Equal(3, initialized.Value.Combat.GetActor("hero")!.GetResource("energy")!.Current);
        Assert.Equal(0, combat.GetActor("hero")!.GetResource("energy")!.Current);

        var first = boundaries.AdvanceActivation(
            initialized.Value.Run,
            initialized.Value.Combat,
            initialized.Value.Run.Deck,
            initialized.Value.Run.Determinism,
            sequence,
            policies,
            TurnPolicy());
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(first.Value.Steps.Count >= 3);
        Assert.Equal("enemy", first.Value.Combat.ActivationState!.ActiveActorId);
        Assert.False(first.Value.Combat.ActivationState.WaitingForInput);
        Assert.Equal(["retain"], first.Value.Deck.Hand);
        Assert.Equal(["strike"], first.Value.Deck.DiscardPile);

        var secondRun = initialized.Value.Run with
        {
            Deck = first.Value.Deck,
            Determinism = first.Value.Steps[^1].RunDeterminism!.AdvanceStep()
        };
        var second = boundaries.AdvanceActivation(
            secondRun,
            first.Value.Combat,
            first.Value.Deck,
            secondRun.Determinism,
            sequence,
            policies,
            TurnPolicy());
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal("hero", second.Value.Combat.ActivationState!.ActiveActorId);
        Assert.True(second.Value.Combat.ActivationState.WaitingForInput);
        Assert.Equal(2, second.Value.Combat.ActivationState.Round);
        Assert.Equal(["retain", "strike"], second.Value.Deck.Hand);
        Assert.Empty(second.Value.Deck.DiscardPile);
    }

    [Fact]
    public void AdvanceActivation_ExhaustsEtherealBeforeDiscardingNonRetain()
    {
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            PlayerEntityId = "hero",
            Determinism = DeterministicContext.Create(7UL, "revision")
        };
        var combat = CombatTransitions.Create(
            [Entity("hero", true, 3), Entity("enemy", false, 3)],
            DeterministicContext.Create(8UL, "revision"));
        var boundaries = Boundaries();
        var initialized = boundaries.InitializeTransaction(
            run, combat, Sequence(), Policies(), TurnPolicy()).Value;
        combat = initialized.Combat;
        var deck = new DeckState { Hand = ["ethereal", "strike", "retain"] };

        var result = boundaries.AdvanceActivation(
            initialized.Run,
            combat,
            deck,
            initialized.Run.Determinism,
            Sequence(),
            Policies(),
            TurnPolicy());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["retain"], result.Value.Deck.Hand);
        Assert.Equal(["strike"], result.Value.Deck.DiscardPile);
        Assert.Equal(["ethereal"], result.Value.Deck.ExhaustPile);
    }

    private static Result<ActionDefinition> ResolveAction(string id) =>
        Result<ActionDefinition>.Success(new ActionDefinition
        {
            ActionId = id,
            Tags = id switch
            {
                "retain" => ["retain"],
                "ethereal" => ["ethereal"],
                _ => []
            }
        });

    private static PhaseSequenceDefinition Sequence() => new()
    {
        SequenceId = "test",
        EntryPhaseId = "start",
        Phases =
        [
            new PhaseDefinition
            {
                PhaseId = "start",
                Role = PhaseRole.Start,
                Order = 10,
                Edges = [new()
                {
                    EdgeId = "start-action", TargetPhaseId = "action",
                    Trigger = PhaseEdgeTrigger.Automatic
                }]
            },
            new PhaseDefinition
            {
                PhaseId = "action",
                Role = PhaseRole.Middle,
                Order = 20,
                Edges = [new()
                {
                    EdgeId = "action-end", TargetPhaseId = "end",
                    Trigger = PhaseEdgeTrigger.ActivationExit
                }]
            },
            new PhaseDefinition
            {
                PhaseId = "end",
                Role = PhaseRole.End,
                Order = 30
            }
        ]
    };

    private static CombatFlowPoliciesDefinition Policies() => new()
    {
        DeckCycle = new()
        {
            DrawPerActivation = 1,
            HandLimit = 10,
            InitialHandSize = 0,
            ActorScope = FlowActorScope.RunOwner,
            EncounterStart = EncounterDeckStartStrategy.PreserveZones,
            EncounterCleanup = EncounterDeckCleanupStrategy.PreserveZones,
            ExhaustPersistence = ExhaustPersistenceStrategy.Encounter,
            GeneratedCardPersistence = GeneratedCardPersistenceStrategy.Encounter,
            EndDiscard = DeckEndDiscardStrategy.NonRetain,
            RetainTags = ["retain"],
            EtherealTag = "ethereal",
            ShuffleDiscardWhenDrawEmpty = true,
            AllowPartialDraw = true,
            Fatigue = FatigueStrategy.None
        },
        ResourceCycle = new()
        {
            ResourceId = "energy",
            ActorScope = FlowActorScope.All,
            StartActivation = ResourceRefreshStrategy.ResetToMax
        },
        Outcome = new()
        {
            EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction,
            TieBreak = OutcomeTieBreak.Draw
        }
    };

    private static TurnOrderPolicyDefinition TurnPolicy() => new()
    {
        Strategy = TurnOrderStrategy.Fixed,
        RecalculateAt = TurnOrderRecalculationBoundary.CombatStart,
        TieBreak = new() { Strategy = TurnOrderTieBreakStrategy.StableActorId }
    };

    private static ITurnOrderResolver TurnOrders() =>
        new TurnOrderResolver(Mock.Of<IRuntimeFormulaEvaluator>());

    private static ICombatBoundaryExecutor Boundaries()
    {
        var actions = new Mock<IActionManager>();
        actions.Setup(item => item.GetDefinition(It.IsAny<string>()))
            .Returns((string id) => ResolveAction(id));
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var triggers = new EffectTriggerExecutor(formulas, new ImmutableEffectProcessor());
        return new CombatBoundaryExecutor(
            TurnOrders(),
            actions.Object,
            new CombatStatusLifecycle(triggers),
            new CombatRelicLifecycle(triggers),
            new CombatResourceLifecycle(triggers),
            new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver());
    }

    private static CombatActorState Entity(string id, bool isHero, float energy)
    {
        static ResourcePool Pool(string id, float current, float maximum, ResourceCategory category) => new()
        {
            ResourceId = id,
            Current = current,
            Maximum = maximum,
            Minimum = 0,
            Definition = new ResourceDefinition
            {
                ResourceId = id,
                DisplayName = id,
                Category = category,
                DefaultMax = maximum
            }
        };

        return new CombatActorState
        {
            InstanceId = id,
            Name = id,
            SideId = isHero ? "player" : "opposition", ControllerBinding = new ControllerBinding { Kind = isHero ? ControllerKind.Player : ControllerKind.AI },
            ResourceState = new ResourceSet
            {
                OwnerId = id,
                Resources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = Pool("health", 20, 20, ResourceCategory.VITAL),
                    ["energy"] = Pool("energy", energy, 3, ResourceCategory.TACTICAL)
                }
            }
        };
    }
}
