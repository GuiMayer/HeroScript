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
using Core.Run.Content;
using Core.CardZones;
using Moq;

namespace Core.Tests.Combat.Flow;

public sealed class CombatFlowPlannerTests
{
    [Fact]
    public void EncounterInitialization_RecordsConfiguredZoneMovesInItsDeterministicTrace()
    {
        var definition = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "test-zones",
            Zones =
            [
                new CardZoneDefinition { ZoneId = "reserve", OwnerScope = CardZoneOwnerScope.RunOwner,
                    Ordering = CardZoneOrdering.Ordered },
                new CardZoneDefinition { ZoneId = "active", OwnerScope = CardZoneOwnerScope.RunOwner,
                    Ordering = CardZoneOrdering.Ordered, AllowsCardPlay = true }
            ],
            Flows = [new CardZoneFlowDefinition
            {
                FlowId = "prepare-encounter", Triggers = ["encounter.started"],
                AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                Steps = [new CardZoneFlowStepDefinition
                {
                    StepId = "move", Operation = CardZoneOperation.Move,
                    SourceZoneId = "reserve", TargetZoneId = "active",
                    Selection = new CardZoneSelectionDefinition
                    {
                        Strategy = CardZoneSelectionStrategy.First,
                        TargetZoneCountFormula = "initialPlayableCardCount"
                    }
                }]
            }]
        };
        var formulas = new Mock<IRuntimeFormulaEvaluator>();
        formulas.Setup(value => value.Evaluate("initialPlayableCardCount",
                It.IsAny<Dictionary<string, float>?>(), It.IsAny<float>()))
            .Returns((string _, Dictionary<string, float>? variables, float _) =>
                Result<float>.Success(variables!["initialPlayableCardCount"]));
        var executor = new CardZoneFlowExecutor(new CardZoneRuntimeRuleEvaluator(
            formulas.Object, Mock.Of<ICardZoneCardMetadataResolver>()));
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(definition).Value,
            new CardZoneBootstrapPlan
            {
                RunOwnerId = "$run",
                Batches =
                [
                    new CardZoneInitialBatch
                    {
                        ZoneId = "reserve", OwnerId = "$run", DefinitionIds = ["skill"]
                    },
                    new CardZoneInitialBatch
                    {
                        ZoneId = "active", OwnerId = "$run", DefinitionIds = ["ready-skill"]
                    }
                ]
            }, DeterministicContext.Create(99UL, "revision")).Value;
        var run = new RunState
        {
            PlayerEntityId = "hero",
            Determinism = seeded.Context,
            Deck = new DeckState { Topology = seeded.State },
            ResolvedMode = new ResolvedGameMode { CardZoneSystem = definition }
        };
        var combat = CombatTransitions.Create(
            [Entity("hero", true, 1), Entity("enemy", false, 1)],
            DeterministicContext.Create(42UL, "revision"));

        var policies = Policies() with
        {
            DeckCycle = Policies().DeckCycle with { InitialPlayableCardCount = 2 }
        };
        var first = Boundaries(executor).InitializeTransaction(run, combat,
            Sequence(), policies, TurnPolicy());
        var repeated = Boundaries(executor).InitializeTransaction(run, combat,
            Sequence(), policies, TurnPolicy());

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(first.Value.Fingerprint, repeated.Value.Fingerprint);
        var move = Assert.Single(first.Value.CardZoneSteps);
        Assert.Equal("prepare-encounter", move.FlowId);
        Assert.Equal("$run::$run::reserve", move.SourceAddress);
        Assert.Equal("$run::$run::active", move.TargetAddress);
        Assert.Equal(2, first.Value.Run.Deck.Topology.GetZone("active", "$run")!.InstanceIds.Count);
        Assert.Single(run.Deck.Topology.GetZone("reserve", "$run")!.InstanceIds);
    }

    [Fact]
    public void LifecycleDeckChangesSurviveInitializationAndActivationPlanning()
    {
        var zones = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "lifecycle-zones",
            Zones =
            [
                new() { ZoneId = "reserve", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered },
                new() { ZoneId = "active", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered }
            ],
            Flows = [new()
            {
                FlowId = "relic.activate",
                AllowedInvocations = [CardZoneFlowInvocation.Effect],
                Steps = [new()
                {
                    StepId = "move",
                    Operation = CardZoneOperation.Move,
                    SourceZoneId = "reserve",
                    TargetZoneId = "active",
                    SourceOwner = CardZoneOwnerBinding.RunOwner,
                    TargetOwner = CardZoneOwnerBinding.RunOwner,
                    Selection = new() { Strategy = CardZoneSelectionStrategy.First, Count = 1 }
                }]
            }]
        };
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
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var zoneFlows = new CardZoneFlowExecutor();
        var triggers = new EffectTriggerExecutor(
            formulas, new ImmutableEffectProcessor(), allowUnconfiguredCalculations: true,
            cardZoneFlows: zoneFlows);
        var boundaries = new CombatBoundaryExecutor(TurnOrders(),
            new CombatStatusLifecycle(triggers), new CombatRelicLifecycle(triggers),
            new CombatResourceLifecycle(triggers), new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver(), zoneFlows);
        var planner = new CombatFlowPlanner(runtimes.Object, boundaries, Mock.Of<IIntentResolver>());
        var cards = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new CardZoneBootstrapPlan
            {
                RunOwnerId = "$run",
                Batches = [new CardZoneInitialBatch
                    { ZoneId = "reserve", OwnerId = "$run", DefinitionIds = ["strike", "strike", "strike"] }]
            }, DeterministicContext.Create(99, "revision")).Value;
        var policies = Policies() with { Ai = new() { PublishIntents = false }, DeckCycle = Policies().DeckCycle with
            { InitialPlayableCardCount = 0, EncounterStart = EncounterDeckStartStrategy.ResetOrdered,
                DrawPerActivation = 0, EndDiscard = DeckEndDiscardStrategy.None } };
        var run = new RunState
        {
            RunId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), PlayerEntityId = "hero",
            Deck = new DeckState { Topology = cards.State },
            Determinism = cards.Context,
            ResolvedMode = new() { CardZoneSystem = zones, CombatRules = new()
                { DefaultPhaseSequenceId = "test", TurnOrder = TurnPolicy(), Flow = policies } },
            Relics = [new()
            {
                RelicInstanceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), DefinitionId = "draw-on-boundary",
                Owner = new() { Kind = GameplayOwnerKind.Entity, Id = "hero" }, ContentRevision = "revision",
                Triggers = new[] { CombatTriggerBoundaries.CombatStart, "EndActivation" }.Select(boundary => new EffectTriggerDefinition
                {
                    TriggerId = boundary, Boundary = boundary,
                    Effects = [new()
                    {
                        Type = EffectType.CARD_ZONE_FLOW,
                        Target = EffectTarget.SELF,
                        CardZoneFlowId = "relic.activate"
                    }]
                }).ToImmutableArray()
            }]
        };
        var combat = CombatTransitions.Create([Entity("hero", true, 0), Entity("enemy", false, 0)],
            DeterministicContext.Create(42, "revision"));
        var initialized = planner.InitializeTransaction(run, combat);
        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        Assert.Single(initialized.Value.Run.Deck.Topology.GetZone("active", "$run")!.InstanceIds);
        Assert.NotEmpty(initialized.Value.EffectSteps);
        Assert.NotEmpty(initialized.Value.Applications);
        Assert.Equal(64, initialized.Value.Fingerprint.Length);
        Assert.Equal(
            Enumerable.Range(0, initialized.Value.EffectSteps.Count),
            initialized.Value.EffectSteps.Select(step => step.Index));
        var advanced = planner.AdvanceActivation(initialized.Value.Run, initialized.Value.Combat,
            initialized.Value.Run.Deck, initialized.Value.Run.Determinism);
        Assert.True(advanced.IsSuccess, advanced.IsFailure ? advanced.Error : null);
        Assert.Equal(2, advanced.Value.Deck.Topology.GetZone("active", "$run")!.InstanceIds.Count);
        Assert.Equal(2, advanced.Value.Steps.Last(step => step.RunSnapshot != null).RunSnapshot!
            .Deck.Topology.GetZone("active", "$run")!.InstanceIds.Count);
        Assert.Empty(run.Deck.Topology.GetZone("active", "$run")!.InstanceIds);
        Assert.Equal("enemy", advanced.Value.Combat.ActivationState!.ActiveActorId);
    }

    [Fact]
    public void InitializeAndAdvance_UseAuthoredZoneFlowsWithoutMutatingInputs()
    {
        var context = DeterministicContext.Create(42UL, "revision");
        var combat = CombatTransitions.Create(
            [Entity("hero", isHero: true, energy: 0), Entity("enemy", isHero: false, energy: 0)],
            context);
        var zones = LifecycleZones();
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new CardZoneBootstrapPlan
            {
                RunOwnerId = "$run",
                Batches =
                [
                    new() { ZoneId = "draw", OwnerId = "$run", DefinitionIds = ["future"] },
                    new() { ZoneId = "hand", OwnerId = "$run", DefinitionIds = ["strike", "retain"] }
                ]
            }, DeterministicContext.Create(99UL, "revision")).Value;
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            PlayerEntityId = "hero",
            Deck = new DeckState { Topology = seeded.State },
            Determinism = seeded.Context,
            ResolvedMode = new() { CardZoneSystem = zones }
        };
        var sequence = Sequence();
        var policies = Policies();

        var boundaries = Boundaries(ZoneExecutor());
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
        Assert.Equal(["retain", "future"], first.Value.Deck.Hand);
        Assert.Equal(["strike"], first.Value.Deck.DiscardPile);
        Assert.Equal(["strike", "retain"], run.Deck.Hand);
    }

    [Fact]
    public void AdvanceActivation_AppliesPriorityOrderedAuthoredEndFlows()
    {
        var zones = LifecycleZones();
        var seeded = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new CardZoneBootstrapPlan
            {
                RunOwnerId = "$run",
                Batches = [new()
                {
                    ZoneId = "hand", OwnerId = "$run",
                    DefinitionIds = ["ethereal", "strike", "retain"]
                }]
            }, DeterministicContext.Create(7UL, "revision")).Value;
        var run = new RunState
        {
            RunId = Guid.NewGuid(),
            PlayerEntityId = "hero",
            Deck = new DeckState { Topology = seeded.State },
            Determinism = seeded.Context,
            ResolvedMode = new() { CardZoneSystem = zones }
        };
        var combat = CombatTransitions.Create(
            [Entity("hero", true, 3), Entity("enemy", false, 3)],
            DeterministicContext.Create(8UL, "revision"));
        var boundaries = Boundaries(ZoneExecutor());
        var initialized = boundaries.InitializeTransaction(
            run, combat, Sequence(), Policies(), TurnPolicy()).Value;
        combat = initialized.Combat;
        var deck = initialized.Run.Deck;

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

    private static CardZoneSystemDefinition LifecycleZones() => new()
    {
        CardZoneSystemId = "lifecycle",
        Zones =
        [
            new() { ZoneId = "draw", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered },
            new() { ZoneId = "hand", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered,
                AllowsCardPlay = true },
            new() { ZoneId = "discard", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered },
            new() { ZoneId = "exhaust", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Unordered }
        ],
        Flows =
        [
            new()
            {
                FlowId = "activation.ethereal", Priority = 50, Triggers = ["activation.ended"],
                AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                Steps = [new()
                {
                    StepId = "move-ethereal", Operation = CardZoneOperation.Move,
                    SourceZoneId = "hand", TargetZoneId = "exhaust",
                    Selection = new() { Strategy = CardZoneSelectionStrategy.ByTags,
                        RequiredTags = ["ethereal"], SelectAllMatches = true },
                    OnInsufficient = CardZoneInsufficientPolicy.AllowPartial
                }]
            },
            new()
            {
                FlowId = "activation.discard", Priority = 100, Triggers = ["activation.ended"],
                AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                Steps = [new()
                {
                    StepId = "move-unplayed", Operation = CardZoneOperation.Move,
                    SourceZoneId = "hand", TargetZoneId = "discard",
                    Selection = new() { Strategy = CardZoneSelectionStrategy.ByTags,
                        ExcludedTags = ["retain", "ethereal"], SelectAllMatches = true },
                    OnInsufficient = CardZoneInsufficientPolicy.AllowPartial
                }]
            },
            new()
            {
                FlowId = "activation.draw", Priority = 100, Triggers = ["activation.started"],
                AllowedInvocations = [CardZoneFlowInvocation.Boundary],
                Steps = [new()
                {
                    StepId = "draw-one", Operation = CardZoneOperation.Move,
                    SourceZoneId = "draw", TargetZoneId = "hand",
                    Selection = new() { Strategy = CardZoneSelectionStrategy.Top, Count = 1 },
                    OnInsufficient = CardZoneInsufficientPolicy.AllowPartial
                }]
            }
        ]
    };

    private static ICardZoneFlowExecutor ZoneExecutor() => new CardZoneFlowExecutor(
        new CardZoneRuntimeRuleEvaluator(Mock.Of<IRuntimeFormulaEvaluator>(), new AuthoredTags()));

    private sealed class AuthoredTags : ICardZoneCardMetadataResolver
    {
        public Result<IReadOnlyList<string>> ResolveTags(
            CardInstanceState instance, CardZoneFlowContext context) =>
            Result<IReadOnlyList<string>>.Success(instance.DefinitionId switch
            {
                "retain" => ["retain"],
                "ethereal" => ["ethereal"],
                _ => []
            });
    }

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
            InitialPlayableCardCount = 0,
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

    private static ICombatBoundaryExecutor Boundaries(ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        var formulas = Mock.Of<IRuntimeFormulaEvaluator>();
        var triggers = new EffectTriggerExecutor(
            formulas, new ImmutableEffectProcessor(), allowUnconfiguredCalculations: true,
            cardZoneFlows: cardZoneFlows);
        return new CombatBoundaryExecutor(
            TurnOrders(),
            new CombatStatusLifecycle(triggers),
            new CombatRelicLifecycle(triggers),
            new CombatResourceLifecycle(triggers),
            new PhaseGraphReducer(formulas, triggers),
            new CombatOutcomeResolver(), cardZoneFlows);
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
