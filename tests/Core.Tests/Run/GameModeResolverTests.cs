using Core.Common;
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
            new Catalog<CombatRulesDefinition>(new CombatRulesDefinition { CombatRulesId = "combat" }),
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

    private sealed class Catalog<TDefinition>(TDefinition definition) : IResourceCatalog<TDefinition>
    {
        public Result<TDefinition> Get(string id, string configName) => Result<TDefinition>.Success(definition);
        public IReadOnlyList<string> Discover(string configName) => [];
        public IReadOnlyList<TDefinition> GetAll(string configName) => [definition];
        public void Invalidate(string? id = null) { }
    }
}
