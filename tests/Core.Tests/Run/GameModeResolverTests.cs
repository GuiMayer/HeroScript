using Core.Common;
using Core.Combat.Flow;
using Core.Config;
using Core.Run;
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

    private static GameModeResolver CreateResolver(
        GameModeDefinition mode,
        ReplayPolicyDefinition? replay = null,
        ContentBindingPolicyDefinition? binding = null)
    {
        return new GameModeResolver(
            new Catalog<GameModeDefinition>(mode),
            new Catalog<FlowRulesDefinition>(new FlowRulesDefinition { FlowRulesId = "flow" }),
            new Catalog<CombatRulesDefinition>(CreateCombatRules()),
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
            }));
    }

    private static CombatRulesDefinition CreateCombatRules() => new()
    {
        CombatRulesId = "combat",
        DefaultActivationRulesId = "activation",
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
                ActorScope = FlowActorScope.Player,
                ResourceId = "energy",
                ConsumingCommands = ["EXECUTE_ACTION"]
            },
            Ai = new() { Enabled = true },
            DeckCycle = new()
            {
                HandLimit = 10,
                ActorScope = FlowActorScope.Player,
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
