using Core.Determinism;
using Core.Run;
using Core.Run.Branching;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunBranchTransitionsTests
{
    [Fact]
    public void Create_IsReproducibleAndLeavesSourceUntouched()
    {
        var source = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000010"),
            Sequence = 7,
            PlayerEntityId = "player",
            Gold = 25,
            Determinism = DeterministicContext.Create(123, "content-v1").AdvanceStep()
        };
        var command = new RunBranchStartCommand(
            source.RunId,
            source.Sequence,
            "alternate",
            CanonicalJson.ComputeHash(source));

        var first = RunBranchTransitions.Create(source, command).Value;
        var second = RunBranchTransitions.Create(source, command).Value;

        Assert.Equal(first, second);
        Assert.Equal(7, source.Sequence);
        Assert.Null(source.ParentRunId);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(source.RunId, first.ParentRunId);
        Assert.Equal(source.Sequence, first.BranchFromSequence);
        Assert.NotEqual(source.RunId, first.RunId);
        Assert.True(first.Determinism.Step > source.Determinism.Step);
    }

    [Fact]
    public void Create_RejectsChangedSourceAndActiveEncounter()
    {
        var source = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000011"),
            Sequence = 2,
            ActiveEncounterId = Guid.Parse("20000000-0000-0000-0000-000000000011"),
            Determinism = DeterministicContext.Create(456, "content-v1")
        };
        var command = new RunBranchStartCommand(
            source.RunId,
            source.Sequence,
            "unsafe",
            CanonicalJson.ComputeHash(source));

        Assert.True(RunBranchTransitions.Create(source, command).IsFailure);
        Assert.True(RunBranchTransitions.Create(
            source with { ActiveEncounterId = null },
            command).IsFailure);
    }
}
