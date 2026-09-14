using API.Models;
using API.Services;
using Core.Run;
using Xunit;

namespace API.Tests.Controllers;

public sealed class ToolAccessPolicyTests
{
    [Theory]
    [InlineData("normal", false, false, false)]
    [InlineData("experimental", true, false, false)]
    [InlineData("sandbox", true, true, false)]
    [InlineData("dev_modder", true, true, true)]
    public void PresetsExposeTheExpectedEscalation(
        string profile,
        bool cheats,
        bool branches,
        bool administration)
    {
        var policy = new ToolAccessPolicy(new ToolAccessSettings { Profile = profile });

        Assert.Equal(cheats, policy.Allows(ToolCapabilities.CheatRunResources));
        Assert.Equal(branches, policy.Allows(ToolCapabilities.BranchCreate));
        Assert.Equal(administration, policy.Allows(ToolCapabilities.AdminOperations));
        Assert.True(policy.Allows(ToolCapabilities.TimelineRead));
    }

    [Fact]
    public void CustomProfileAddsCapabilityDependencies()
    {
        var policy = new ToolAccessPolicy(new ToolAccessSettings
        {
            Profile = "custom",
            CustomCapabilities = [ToolCapabilities.BranchCreate, ToolCapabilities.TimelineInspectState]
        });

        Assert.True(policy.Allows(ToolCapabilities.BranchCreate));
        Assert.True(policy.Allows(ToolCapabilities.BranchRead));
        Assert.True(policy.Allows(ToolCapabilities.TimelineInspectState));
        Assert.True(policy.Allows(ToolCapabilities.TimelineRead));
        Assert.False(policy.Allows(ToolCapabilities.CheatCardZones));
    }

    [Fact]
    public void RunCapabilitiesAreTheIntersectionWithTheModeCeiling()
    {
        var policy = new ToolAccessPolicy(new ToolAccessSettings { Profile = "dev_modder" });
        var run = new RunState
        {
            ResolvedMode = new ResolvedGameMode
            {
                ReplayPolicy = new ReplayPolicyDefinition
                {
                    TimelineAccess = "summary",
                    SemanticVerification = true
                },
                TimelinePolicy = new TimelinePolicyDefinition { Enabled = true },
                CapabilityPolicy = new CapabilityPolicyDefinition()
            }
        };

        Assert.True(policy.Allows(run, ToolCapabilities.TimelineRead));
        Assert.True(policy.Allows(run, ToolCapabilities.ReplayVerify));
        Assert.False(policy.Allows(run, ToolCapabilities.BranchCreate));
        Assert.False(policy.Allows(run, ToolCapabilities.CheatRunResources));
    }
}
