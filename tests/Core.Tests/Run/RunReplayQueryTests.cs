using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;
using Core.Run.Replay;
using Moq;
using Xunit;

namespace Core.Tests.Run;
public class RunReplayQueryTests
{
    [Fact]
    public async Task TimelineUsesPagedEnvelopesAndNeverLeaksStateOrFramePayload()
    {
        var run = Guid.NewGuid();
        var state = new RunState { RunId = run, SettingId = "custom", Determinism = DeterministicContext.Create(1,"revision") };
        var commit = new RunCommit { RunId = run, Sequence = 1, StateAfter = state,
            RootCommand = new(Guid.NewGuid(),"custom",0,0,"hash"),
            Frames = [new() { FrameIndex=0, Scope="combat", Kind="custom", Resolution=JsonSerializer.SerializeToElement(new { secret="heavy" }) }] };
        var source = new Mock<IRunCommitEnvelopeReader>(MockBehavior.Strict);
        source.Setup(s => s.LoadEnvelopePageAsync(run,0,25,null,It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunCommitEnvelopePage(run,0,100,1,true,commit,[commit]));
        var result = await new RunReplayQueryService(source.Object).ReadTimelineAsync(run,0,25);
        Assert.NotNull(result); Assert.True(result.HasMore); Assert.Equal(100,result.ThroughSequence);
        Assert.Equal("custom",result.SettingId);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("stateAfter",json); Assert.DoesNotContain("resolution",json);
        Assert.DoesNotContain("calculations",json); Assert.DoesNotContain("heavy",json);
        source.VerifyAll();
    }
}
