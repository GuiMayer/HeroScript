using Core.Common;
using Core.Combat.Flow;
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
            CapabilityPolicyId = "capabilities"
        });

        var result = resolver.Resolve("sandbox", "test");

        Assert.True(result.IsSuccess);
        Assert.Equal("sandbox", result.Value.Definition.ModeId);
        Assert.Equal("flow", result.Value.FlowRules.FlowRulesId);
        Assert.Equal("combat", result.Value.CombatRules.CombatRulesId);
        Assert.True(result.Value.CapabilityPolicy.AllowTimelineFork);
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
                CapabilityPolicyId = "capabilities"
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
                CapabilityPolicyId = "capabilities"
            },
            binding: new ContentBindingPolicyDefinition
            {
                ContentBindingPolicyId = "binding",
                ActiveRuns = "pinned"
            });

        var result = resolver.Resolve("invalid", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("hot reload", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_WarnsAndRejectsReservedReactionStrategy()
    {
        var logger = new Mock<ILogger>();
        var combat = CreateCombatRules();
        combat = combat with
        {
            Flow = combat.Flow with
            {
                Reactions = new ReactionPolicyDefinition { Strategy = ReactionStrategy.Stack }
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
                CapabilityPolicyId = "capabilities"
            },
            combat: combat,
            logger: logger.Object);

        var result = resolver.Resolve("reserved", "test");

        Assert.True(result.IsFailure);
        logger.Verify(item => item.LogWarning(
            It.Is<string>(message => message.Contains("reactions are not implemented", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void Resolve_WarnsAndRejectsReservedOutcomeBoundary()
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
                CapabilityPolicyId = "capabilities"
            },
            combat: combat,
            logger: logger.Object);

        var result = resolver.Resolve("reserved", "test");

        Assert.True(result.IsFailure);
        logger.Verify(item => item.LogWarning(
            It.Is<string>(message => message.Contains("only AfterCurrentAction is implemented", StringComparison.Ordinal))),
            Times.Once);
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
                ActiveRuns = "allow_versioned_activation"
            }),
            new Catalog<CapabilityPolicyDefinition>(new CapabilityPolicyDefinition
            {
                CapabilityPolicyId = "capabilities",
                AllowTimelineFork = true,
                AllowHotReloadActivation = true
            }),
            logger: logger);
    }

    private static CombatRulesDefinition CreateCombatRules() => new()
    {
        CombatRulesId = "combat",
        DefaultPhaseSequenceId = "phases",
        Flow = new CombatFlowPoliciesDefinition
        {
            AutomaticResolution = new()
            {
                Strategy = AutomaticResolutionStrategy.ToNextPlayerInput,
                MaxAutomaticSteps = 100
            },
            ActivationOrder = new()
            {
                Strategy = ActivationOrderStrategy.RoundSnapshot,
                TieBreak = ActivationTieBreak.StableActorId
            },
            ActionBudget = new()
            {
                Strategy = ActionBudgetStrategy.ResourceLimited,
                ActionCosts = ActionCostStrategy.Configured,
                ActorScope = FlowActorScope.Player,
                ResourceId = "energy",
                ConsumingCommands = ["EXECUTE_ACTION"]
            },
            Ai = new() { Enabled = true },
            DeckCycle = new()
            {
                HandLimit = 10,
                InitialHandSize = 5,
                ActorScope = FlowActorScope.Player,
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

    private sealed class Catalog<TDefinition>(TDefinition definition) : IResourceCatalog<TDefinition>
    {
        public Result<TDefinition> Get(string id, string configName) => Result<TDefinition>.Success(definition);
        public IReadOnlyList<string> Discover(string configName) => [];
        public IReadOnlyList<TDefinition> GetAll(string configName) => [definition];
        public void Invalidate(string? id = null) { }
    }
}
