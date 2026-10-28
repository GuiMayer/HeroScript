using Core.Determinism;
using Xunit;
namespace Core.Tests.GameplayEvents;
/// <summary>Golden outputs captured from pre-migration tree c30b806d16fc77e69936b880e207c26c7bb7afdd.</summary>
public sealed class BoundaryMigrationParityTests
{
    [Theory]
    [InlineData("enter", "64794eec202f54d90084ec455c1d25cbb617a54724b12421c1ce19761cfb7279")]
    [InlineData("exit", "bae8e4d18c59e42d705e0abe267ccd6631252ec5fff4fc918b64faf5d278f94f")]
    [InlineData("command", "3379522f8fa63eebf03dff435aa8b305136efa0c506e520aa093b7458cdfd350")]
    public void PhaseEntryExitAndCommandPreserveTheEntireExecution(string scenario, string expected)
    {
        var f = BoundaryMigrationFixtures.Phase();
        var input = CanonicalJson.ComputeHash(new { f.Run, f.Combat });
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = f.Reducer.Enter(f.Run, f.Combat, f.Graph);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            if (scenario == "exit") result = f.Reducer.Exit(result.Value.Run, result.Value.Combat, f.Graph);
            if (scenario == "command") result = f.Reducer.HandleCommand(result.Value.Run, result.Value.Combat, f.Graph,
                new() { ActorId = "hero", ActionType = Core.Combat.Models.ActionType.PASS }, new HashSet<string>());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(expected, CanonicalJson.ComputeHash(result.Value));
            Assert.Equal(input, CanonicalJson.ComputeHash(new { f.Run, f.Combat }));
        }
    }

    [Theory]
    [InlineData("empty", "988a311811303cf1ddc7d7b0ba61e0bf822681df0310474bbe8c63adfd1b3b80")]
    [InlineData("expire", "fc4dd4fe2f4d91772aa1e0367d8b34eda87563351ce317314cf28223070872d0")]
    [InlineData("missing-source", "1da76269e44c107d36e44c897f442a73b75d6ca88c6cc255fdf99845e98863fb")]
    [InlineData("permanent", "10fc1622a77ba2593f45ba5916211df784dc7c2c26c3f3d914056f377bad25a9")]
    [InlineData("remove", "25d18eaca76ab5cfe50cba7aab9eb1867bc9415415c18928c7687b314e38d7a2")]
    [InlineData("round", "3954f0888ce1f58f51387fbd63c0ec4025b7abce2add8d95a5a31581f903b991")]
    [InlineData("tie", "cb12c00c13037d5765c5b53616d80248dcf92db7a7e2becfcbf8b4fa99e13022")]
    public void CapturedStatusLifecyclePreservesTheEntireExecution(string scenario, string expected)
    {
        var f = BoundaryMigrationFixtures.Status(scenario);
        var input = CanonicalJson.ComputeHash(new { f.Run, f.Combat });
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = f.Lifecycle.Process(f.Run, f.Combat, f.Boundary, "enemy");
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal(expected, CanonicalJson.ComputeHash(result.Value));
            Assert.Equal(input, CanonicalJson.ComputeHash(new { f.Run, f.Combat }));
        }
    }
    [Fact]
    public void CapturedRelicPriorityOwnersAndIdentitiesRemainExactlyEqual()
    {
        var f = BoundaryMigrationFixtures.Relics();
        var input = CanonicalJson.ComputeHash(new { f.Run, f.Combat });
        for (var repeat = 0; repeat < 5; repeat++)
        {
            var result = f.Lifecycle.Process(f.Run, f.Combat, "CombatStart");
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
            Assert.Equal("076a84e7470a763df38ee3e6876cdafe6599d69a534beb52a3bbf7e7d2292c55", CanonicalJson.ComputeHash(result.Value));
            Assert.Equal(input, CanonicalJson.ComputeHash(new { f.Run, f.Combat }));
        }
    }
}
