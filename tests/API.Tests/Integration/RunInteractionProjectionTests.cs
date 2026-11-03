using API.Models;
using System.Collections.Immutable;
using Core.Determinism;
using Core.Combat.Models;
using Core.Run;
using Core.Run.Projections;
using Xunit;

namespace API.Tests.Integration;

public sealed class RunInteractionProjectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetrySummaryIsDerivedFromLatestCurrentAttemptWithoutExposingHistory(bool resolved)
    {
        var combat = new CombatState { CombatId = Guid.NewGuid(), Status = CombatStatus.DEFEAT };
        var run = new RunState { CurrentNodeId = "fight", ActiveEncounterId = resolved ? null : combat.CombatId,
            Encounters = [new() { NodeId = "old", Combat = combat },
                new() { NodeId = "fight", Combat = combat, Resolved = resolved }],
            ResolvedMode = new() { ProgressionPolicy = new() { EncounterRetry = RunEncounterRetryPolicy.RestartActivity,
                RetryableEncounterOutcomes = [CombatStatus.DEFEAT] } } };
        var before = CanonicalJson.ComputeHash(run);
        var projected = RunInteractionProjection.Project(new(run));
        var attempt = projected.GetProperty("encounterAttempt");
        Assert.Equal(1, attempt.GetProperty("number").GetInt32());
        Assert.Equal(combat.CombatId, attempt.GetProperty("combatId").GetGuid());
        Assert.Equal(resolved, attempt.GetProperty("pendingRetry").GetBoolean());
        Assert.Equal(!resolved, attempt.GetProperty("awaitingConfirmation").GetBoolean());
        Assert.False(projected.TryGetProperty("encounters", out _));
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Null(RunEncounterAttempts.Current(run with { Lifecycle = RunLifecycleState.Failed }));
        Assert.Null(RunEncounterAttempts.Current(run with { Map = new() { ResolvedNodeIds = ["fight"] } }));
        Assert.Null(RunEncounterAttempts.Current(run with { Encounters = run.Encounters.Add(new()
            { NodeId = "fight", Combat = combat with { Status = CombatStatus.ACTIVE } }) }));
    }

    [Theory]
    [InlineData(CombatStatus.ACTIVE, 1)]
    [InlineData(CombatStatus.VICTORY, 3)]
    [InlineData(CombatStatus.DEFEAT, 3)]
    [InlineData(CombatStatus.DRAW, 3)]
    [InlineData(CombatStatus.ABANDONED, 3)]
    public void PendingEncounterReferenceOnlyCompactsTheMapWhileCombatIsActive(CombatStatus status, int count)
    {
        var combat = new CombatState { CombatId = Guid.NewGuid(), Status = status };
        var run = new RunState
        {
            RunId = Guid.NewGuid(), CurrentNodeId = "first", ActiveEncounterId = combat.CombatId,
            Encounters = [new RunEncounterState { NodeId = "first", Combat = combat, Resolved = false }],
            Map = new RunMapState { Nodes = [
                new() { NodeId = "first", NextNodeIds = ["reward"] },
                new() { NodeId = "reward", NextNodeIds = ["second"] }, new() { NodeId = "second" }],
                VisitedNodeIds = ["first"], ResolvedNodeIds = [] },
            Determinism = DeterministicContext.Create(123, "revision")
        };
        var context = new InteractionReadContext(run);
        var before = CanonicalJson.ComputeHash(run);
        var projected = RunInteractionProjection.Project(context);
        var map = projected.GetProperty("map");
        Assert.Equal(count, map.GetProperty("nodes").GetArrayLength());
        Assert.Equal("reward", map.GetProperty("nodes")[0].GetProperty("nextNodeIds")[0].GetString());
        Assert.Equal(combat.CombatId, projected.GetProperty("activeEncounterId").GetGuid());
        Assert.Empty(map.GetProperty("resolvedNodeIds").EnumerateArray());
        Assert.Equal(projected.GetRawText(), RunInteractionProjection.Project(context).GetRawText());
        Assert.Equal(before, CanonicalJson.ComputeHash(run));
        Assert.Equal(context.Version, new InteractionReadContext(run).Version);
        Assert.False(run.Encounters[0].Resolved); // A read must not confirm a terminal result.
        Assert.Same(run, context.Run);
    }

    [Fact]
    public void AbsentEncounterSnapshotDoesNotHideTheNavigationMap()
    {
        var run = new RunState { RunId = Guid.NewGuid(), CurrentNodeId = "first", ActiveEncounterId = Guid.NewGuid(),
            Map = new RunMapState { Nodes = [new() { NodeId = "first" }, new() { NodeId = "second" }] } };
        Assert.Equal(2, RunInteractionProjection.Project(new(run)).GetProperty("map").GetProperty("nodes").GetArrayLength());
    }

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
