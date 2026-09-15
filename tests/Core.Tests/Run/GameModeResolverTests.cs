using Core.Common;
using Core.CardZones;
using Core.Combat.Flow;
using Core.Combat.TurnOrder;
using Core.Config;
using Core.Logging;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class GameModeResolverTests
{
    [Fact]
    public void Resolve_ProducesImmutableComposedMode_WhenReferencesAreCompatible()
    {
        var resolver = CreateResolver(new GameModeDefinition
        {
            ModeId = "sandbox",
            FlowRulesId = "flow",
            CombatRulesId = "combat",
            ReplayPolicyId = "replay",
            TimelinePolicyId = "timeline",
            ContentBindingPolicyId = "binding",
            CapabilityPolicyId = "capabilities",
            ProgressionPolicyId = "progression",
            CardZoneSystemId = "zones"
        });

        var result = resolver.Resolve("sandbox", "test");

        Assert.True(result.IsSuccess);
        Assert.Equal("sandbox", result.Value.Definition.ModeId);
        Assert.Equal("flow", result.Value.FlowRules.FlowRulesId);
        Assert.Equal("combat", result.Value.CombatRules.CombatRulesId);
        Assert.True(result.Value.CapabilityPolicy.AllowTimelineFork);
        Assert.Equal("zones", result.Value.CardZoneSystem?.CardZoneSystemId);
    }

    [Fact]
    public void Resolve_RequiresExplicitProgressionPolicy()
    {
        var resolver = CreateResolver(new GameModeDefinition
        {
            ModeId = "missing-progression",
            FlowRulesId = "flow",
            CombatRulesId = "combat",
            ReplayPolicyId = "replay",
            TimelinePolicyId = "timeline",
            ContentBindingPolicyId = "binding",
            CapabilityPolicyId = "capabilities"
        });

        var result = resolver.Resolve("missing-progression", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("progression policy", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsTimelineFork_WhenReplayPolicyDoesNotAllowIt()
    {
        var resolver = CreateResolver(
            new GameModeDefinition
            {
                ModeId = "invalid",
                FlowRulesId = "flow",
                CombatRulesId = "combat",
                ReplayPolicyId = "replay",
                TimelinePolicyId = "timeline",
                ContentBindingPolicyId = "binding",
                CapabilityPolicyId = "capabilities",
                ProgressionPolicyId = "progression"
            },
            replay: new ReplayPolicyDefinition
            {
                ReplayPolicyId = "replay",
                AllowForkFromHistory = false
            });

        var result = resolver.Resolve("invalid", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("timeline forks", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsHotReloadCapability_WhenContentBindingPinsActiveRuns()
    {
        var resolver = CreateResolver(
            new GameModeDefinition
            {
                ModeId = "invalid",
                FlowRulesId = "flow",
                CombatRulesId = "combat",
                ReplayPolicyId = "replay",
                TimelinePolicyId = "timeline",
                ContentBindingPolicyId = "binding",
                CapabilityPolicyId = "capabilities",
                ProgressionPolicyId = "progression"
            },
            binding: new ContentBindingPolicyDefinition
            {
                ContentBindingPolicyId = "binding",
                ActiveRuns = ActiveRunContentBinding.Pinned
            });

        var result = resolver.Resolve("invalid", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("hot reload", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AcceptsConfiguredPriorityStack()
    {
        var logger = new Mock<ILogger>();
        var combat = CreateCombatRules();
        combat = combat with
        {
            Flow = combat.Flow with
            {
                Reactions = PriorityStack()
            }
        };
        var resolver = CreateResolver(
            new GameModeDefinition
            {
                ModeId = "reserved",
                FlowRulesId = "flow",
                CombatRulesId = "combat",
                ReplayPolicyId = "replay",
                TimelinePolicyId = "timeline",
                ContentBindingPolicyId = "binding",
                CapabilityPolicyId = "capabilities",
                ProgressionPolicyId = "progression"
            },
            combat: combat,
            logger: logger.Object);

        var result = resolver.Resolve("reserved", "test");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Resolve_RequiresPriorityStackForStackOutcomeBoundary()
    {
        var logger = new Mock<ILogger>();
        var combat = CreateCombatRules();
        combat = combat with
        {
            Flow = combat.Flow with
            {
                Outcome = new OutcomePolicyDefinition
                {
                    EvaluationBoundary = OutcomeEvaluationBoundary.AfterResolutionStack,
                    TieBreak = OutcomeTieBreak.Draw
                }
            }
        };
        var resolver = CreateResolver(
            new GameModeDefinition
            {
                ModeId = "reserved",
                FlowRulesId = "flow",
                CombatRulesId = "combat",
                ReplayPolicyId = "replay",
                TimelinePolicyId = "timeline",
                ContentBindingPolicyId = "binding",
                CapabilityPolicyId = "capabilities",
                ProgressionPolicyId = "progression"
            },
            combat: combat,
            logger: logger.Object);

        var result = resolver.Resolve("reserved", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("PriorityStack", result.Error, StringComparison.Ordinal);
    }

    private static GameModeResolver CreateResolver(
        GameModeDefinition mode,
        ReplayPolicyDefinition? replay = null,
        ContentBindingPolicyDefinition? binding = null,
        CombatRulesDefinition? combat = null,
        ILogger? logger = null)
    {
        return new GameModeResolver(
            new Catalog<GameModeDefinition>(mode),
            new Catalog<FlowRulesDefinition>(new FlowRulesDefinition { FlowRulesId = "flow" }),
            new Catalog<CombatRulesDefinition>(combat ?? CreateCombatRules()),
            new Catalog<ReplayPolicyDefinition>(replay ?? new ReplayPolicyDefinition
            {
                ReplayPolicyId = "replay",
                AllowForkFromHistory = true
            }),
            new Catalog<TimelinePolicyDefinition>(new TimelinePolicyDefinition
            {
                TimelinePolicyId = "timeline",
                Enabled = true
            }),
            new Catalog<ContentBindingPolicyDefinition>(binding ?? new ContentBindingPolicyDefinition
            {
                ContentBindingPolicyId = "binding",
                ActiveRuns = ActiveRunContentBinding.Versioned
            }),
            new Catalog<CapabilityPolicyDefinition>(new CapabilityPolicyDefinition
            {
                CapabilityPolicyId = "capabilities",
                AllowTimelineFork = true,
                AllowHotReloadActivation = true
            }),
            new Catalog<RunProgressionPolicyDefinition>(new RunProgressionPolicyDefinition
            {
                ProgressionPolicyId = "progression"
            }),
            logger: logger,
            cardZoneSystems: new Catalog<CardZoneSystemDefinition>(new CardZoneSystemDefinition
            {
                CardZoneSystemId = "zones",
                Zones = [new CardZoneDefinition
                {
                    ZoneId = "library",
                    OwnerScope = CardZoneOwnerScope.Actor,
                    Ordering = CardZoneOrdering.Ordered
                }]
            }));
    }

    private static CombatRulesDefinition CreateCombatRules() => new()
    {
        CombatRulesId = "combat",
        DefaultPhaseSequenceId = "phases",
        TurnOrder = new()
        {
            Strategy = TurnOrderStrategy.Fixed,
            RecalculateAt = TurnOrderRecalculationBoundary.CombatStart,
            TieBreak = new() { Strategy = TurnOrderTieBreakStrategy.StableActorId }
        },
        Flow = new CombatFlowPoliciesDefinition
        {
            AutomaticResolution = new()
            {
                Strategy = AutomaticResolutionStrategy.ToNextPlayerInput,
                MaxAutomaticSteps = 100
            },
            ActionBudget = new()
            {
                Strategy = ActionBudgetStrategy.ResourceLimited,
                ActionCosts = ActionCostStrategy.Configured,
                ActorScope = FlowActorScope.PlayerControlled,
                ResourceId = "energy",
                ConsumingCommands = ["EXECUTE_ACTION"]
            },
            Ai = new()
            {
                Enabled = true,
                DecisionIds = ["decision"],
                Intent = new()
                {
                    Refresh = IntentRefreshStrategy.RecomputeOnPublish,
                    WhenInvalid = InvalidIntentStrategy.Recompute
                }
            },
            DeckCycle = new()
            {
                HandLimit = 10,
                InitialHandSize = 5,
                ActorScope = FlowActorScope.RunOwner,
                EncounterStart = EncounterDeckStartStrategy.ResetOrdered,
                EncounterCleanup = EncounterDeckCleanupStrategy.ReturnToDrawPile,
                ExhaustPersistence = ExhaustPersistenceStrategy.Encounter,
                GeneratedCardPersistence = GeneratedCardPersistenceStrategy.Encounter,
                EndDiscard = DeckEndDiscardStrategy.NonRetain,
                Fatigue = FatigueStrategy.None
            },
            ResourceCycle = new()
            {
                ResourceId = "energy",
                ActorScope = FlowActorScope.All,
                StartActivation = ResourceRefreshStrategy.ResetToMax
            },
            StatusTiming = new()
            {
                Boundaries = [StatusTriggerBoundary.StartActivation],
                Ordering = StatusOrderingStrategy.PriorityThenInstanceId
            },
            Outcome = new()
            {
                EvaluationBoundary = OutcomeEvaluationBoundary.AfterCurrentAction,
                TieBreak = OutcomeTieBreak.Draw
            },
            EncounterResolution = new() { Strategy = EncounterResolutionStrategy.ManualAck },
            Animation = new() { Mode = AnimationFrameMode.FullSnapshots },
            Journal = new() { Granularity = CombatJournalGranularity.Full },
            Reactions = new() { Strategy = ReactionStrategy.Disabled }
        }
    };

    private static ReactionPolicyDefinition PriorityStack() => new()
    {
        Strategy = ReactionStrategy.PriorityStack,
        StackOrder = ReactionStackOrder.Lifo,
        Eligibility = ReactionActorEligibility.AllAlive,
        TargetLock = ReactionLockTiming.Proposal,
        CostTiming = ReactionCostTiming.Resolution,
        Failure = ReactionResolutionFailure.FizzleKeepPaid,
        MaxStackDepth = 16,
        ReopenAfterResolution = true
    };

    private sealed class Catalog<TDefinition>(TDefinition definition) : IResourceCatalog<TDefinition>
    {
        public Result<TDefinition> Get(string id, string configName) => Result<TDefinition>.Success(definition);
        public IReadOnlyList<string> Discover(string configName) => [];
        public IReadOnlyList<TDefinition> GetAll(string configName) => [definition];
        public void Invalidate(string? id = null) { }
    }
}
