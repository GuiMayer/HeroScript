using API.Models;
using System.Collections.Immutable;
using Core.Determinism;
using Core.Run;
using Core.Run.Projections;
using Xunit;

namespace API.Tests.Integration;

public sealed class RunInteractionProjectionTests
{
    [Fact]
    public void PastEncountersDoNotInflateTheInteractionContractOrChangeItsSource()
    {
        var run = new RunState { RunId = Guid.NewGuid(), PlayerEntityId = "hero",
            Determinism = DeterministicContext.Create(123, "revision") };
        var context = new InteractionReadContext(run);
        var before = CanonicalJson.ComputeHash(run);
        var first = RunInteractionProjection.Project(context);
        var longRun = run with { Encounters = Enumerable.Range(0, 100).Select(i => new RunEncounterState
            { NodeId = "old-" + i, Resolved = true }).ToImmutableArray() };
        var second = RunInteractionProjection.Project(new(longRun));
        Assert.False(second.TryGetProperty("encounters", out _));
        Assert.False(second.TryGetProperty("contentManifest", out _));
        Assert.Equal(first.GetRawText().Length, second.GetRawText().Length);
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Same(run, context.Run);
        Assert.NotEqual(context.Version.StateFingerprint, new InteractionReadContext(longRun).Version.StateFingerprint);
    }
}
