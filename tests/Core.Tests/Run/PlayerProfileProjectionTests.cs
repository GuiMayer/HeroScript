using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Meta;
using Core.Run;
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

        var profile = await new PlayerProfileProjectionReader(reader.Object).ReadAsync("player");

        var run = Assert.Single(profile.Runs);
        Assert.Equal(validId, run.RunId);
        Assert.Equal(1, profile.TotalRuns);
        Assert.Equal(1, profile.CompletedRuns);
    }
}
