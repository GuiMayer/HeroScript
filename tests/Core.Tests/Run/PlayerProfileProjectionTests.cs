using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Meta;
using Core.Run;
using Core.Run.Branching;
using Moq;
using Xunit;

namespace Core.Tests.Run;

[Trait("Category", "Unit")]
public sealed class PlayerProfileProjectionTests
{
    [Fact]
    public async Task ReadAsync_SkipsUnreadableStreamsWithoutHidingValidRuns()
    {
        var unreadableId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var validId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var reader = new Mock<IRunCommitReader>();
        reader.Setup(source => source.ListRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { unreadableId, validId });
        reader.Setup(source => source.LoadLatestStateAsync(unreadableId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Run commit stateHash does not match stateAfter"));
        reader.Setup(source => source.LoadLatestStateAsync(validId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunState
            {
                RunId = validId,
                Sequence = 7,
                Lifecycle = RunLifecycleState.Completed,
                ConfigName = "default",
                PlayerEntityId = "player",
                ModeId = "showcase",
                CurrentNodeId = "summit",
                Determinism = DeterministicContext.Create(42, "revision")
            });

        var profile = await new PlayerProfileProjectionReader(reader.Object).ReadAsync("player", "default");

        var run = Assert.Single(profile.Runs);
        Assert.Equal(validId, run.RunId);
        Assert.Equal(1, profile.TotalRuns);
        Assert.Equal(1, profile.CompletedRuns);
    }

    [Fact]
    public async Task ReadAsync_IsolatesPermanentProgressAndRevisionByPlayerAndSetting()
    {
        var a = new RunState { RunId = Guid.NewGuid(), PlayerEntityId = "player", SettingId = "a",
            Lifecycle = RunLifecycleState.Completed, Sequence = 5 };
        var b = new RunState { RunId = Guid.NewGuid(), PlayerEntityId = "player", SettingId = "b",
            Lifecycle = RunLifecycleState.Active, Sequence = 2 };
        var other = a with { RunId = Guid.NewGuid(), PlayerEntityId = "other" };
        var simulation = a with { RunId = Guid.NewGuid(), Lineage = new RunLineage { InternalSimulation = true } };
        var states = new[] { a, b, other, simulation }.ToDictionary(run => run.RunId);
        var reader = new Mock<IRunCommitReader>();
        reader.Setup(source => source.ListRunIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(states.Keys.ToArray());
        reader.Setup(source => source.LoadLatestStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => states[id]);
        var profiles = new PlayerProfileProjectionReader(reader.Object);

        var profileA = await profiles.ReadAsync("player", "a");
        var profileB = await profiles.ReadAsync("player", "b");
        Assert.Equal("a", profileA.SettingId);
        Assert.Equal(a.RunId, Assert.Single(profileA.Runs).RunId);
        Assert.Equal("a", profileA.Runs[0].SettingId);
        Assert.Equal(1, profileA.CompletedRuns);
        Assert.Contains("first-completion", profileA.Achievements);
        // Completion is a history badge, not a hardcoded gameplay unlock.
        Assert.Empty(profileA.Unlocks);
        Assert.Equal(b.RunId, Assert.Single(profileB.Runs).RunId);
        Assert.Equal(1, profileB.ActiveRuns);
        Assert.Equal(0, profileB.CompletedRuns);
        Assert.DoesNotContain("first-completion", profileB.Achievements);
        Assert.Empty(profileB.Unlocks);

        states[a.RunId] = a with { Sequence = 9 };
        Assert.Equal(profileB.Revision, (await profiles.ReadAsync("player", "b")).Revision);
        Assert.NotEqual(profileA.Revision, (await profiles.ReadAsync("player", "a")).Revision);
        var emptyC = await profiles.ReadAsync("player", "c");
        var emptyD = await profiles.ReadAsync("player", "d");
        Assert.Empty(emptyC.Runs);
        Assert.Empty(emptyC.Achievements);
        Assert.Empty(emptyC.Unlocks);
        Assert.NotEqual(emptyC.Revision, emptyD.Revision);
        await Assert.ThrowsAsync<ArgumentException>(() => profiles.ReadAsync("player", " "));
    }
}
