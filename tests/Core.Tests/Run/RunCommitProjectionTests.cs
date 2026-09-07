using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;
using Core.Run.Branching;
using Core.Run.Events;
using Core.Run.Projections;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunCommitProjectionTests
{
    [Fact]
    public async Task CombatProjection_UsesPersistedScopeInsteadOfCommandName()
    {
        var runId = Guid.NewGuid();
        var combatId = Guid.NewGuid();
        var unrelatedNamedCommand = Commit(runId, 1, "END_TURN", "run", null);
        var scopedArbitraryCommand = Commit(runId, 2, "CUSTOM_RULE", "combat", combatId);
        var source = new Mock<IRunCommitReader>();
        source.Setup(reader => reader.LoadCommitsAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([unrelatedNamedCommand, scopedArbitraryCommand]);
        var projection = new RunCommitProjectionReader(source.Object);

        var result = await projection.ReadCombatAsync(runId, combatId, 0, 10);

        Assert.Single(result);
        Assert.Equal("CUSTOM_RULE", result[0].RootCommand.Type);
    }

    [Fact]
    public async Task DurableFacts_HaveStableDistinctIdsWithinOneCommit()
    {
        var runId = Guid.NewGuid();
        var combatId = Guid.NewGuid();
        var firstFrame = Frame(0, 1, "combat", combatId);
        var secondFrame = Frame(1, 2, "combat", combatId);
        var commit = Commit(runId, 1, "END_TURN", "combat", combatId) with
        {
            Frames = [firstFrame, secondFrame],
            Facts = RunCommitFacts.FromFrames([firstFrame, secondFrame])
        };
        var commits = new Mock<IRunCommitProjectionReader>();
        commits.Setup(reader => reader.ReadRunAsync(runId, 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        commits.Setup(reader => reader.ReadRunAsync(runId, 0, int.MaxValue, It.IsAny<CancellationToken>()))
            .ReturnsAsync([commit]);
        var projection = new RunEventProjectionReader(commits.Object);

        var first = await projection.ReadRunEventsAsync(runId, RunEventCursor.AfterSequence(0), 10);
        var rebuilt = await projection.ReadRunEventsAsync(runId, RunEventCursor.AfterSequence(0), 10);

        Assert.Equal([0, 1], first.Select(item => item.FactIndex));
        Assert.Equal(2, first.Select(item => item.EventId).Distinct().Count());
        Assert.Equal(first.Select(item => item.EventId), rebuilt.Select(item => item.EventId));
        Assert.All(first, item => Assert.Equal(combatId, item.CombatId));

        var resumed = await projection.ReadRunEventsAsync(runId, new RunEventCursor(1, 0), 10);
        Assert.Single(resumed);
        Assert.Equal(1, resumed[0].FactIndex);
    }

    private static RunCommit Commit(
        Guid runId,
        int sequence,
        string type,
        string scope,
        Guid? combatId)
    {
        var payload = JsonSerializer.SerializeToElement(new { sequence });
        var context = DeterministicContext.Create(17, "revision");
        for (var index = 0; index < sequence; index++)
            context = context.AdvanceStep();
        var state = new RunState
        {
            RunId = runId,
            Sequence = sequence,
            Lineage = RunLineage.Root(runId),
            Determinism = context
        };
        var frame = Frame(0, context.Step, scope, combatId);
        return new RunCommit
        {
            RunId = runId,
            Sequence = sequence,
            RootCommand = new RunCommandIdentity(
                DeterministicId.Create(17, (ulong)sequence, "projection-test"),
                type,
                sequence - 1,
                context.Step - 1,
                CanonicalJson.ComputeHash(payload)),
            Command = payload,
            PreviousStateHash = sequence == 1 ? string.Empty : "previous",
            StateHash = CanonicalJson.ComputeHash(state),
            BeforeStep = context.Step - 1,
            AfterStep = context.Step,
            LogicalTimestamp = context.LogicalTimestamp.UtcDateTime,
            StateAfter = state,
            Lineage = sequence == 1 ? state.Lineage : null,
            Frames = [frame],
            Facts = RunCommitFacts.FromFrames([frame])
        };
    }

    private static RunCommitFrame Frame(int index, ulong step, string scope, Guid? combatId) => new()
    {
        FrameIndex = index,
        Step = step,
        Scope = scope,
        Kind = "test.fact",
        CombatId = combatId,
        ResultHash = $"result-{index}",
        Resolution = JsonSerializer.SerializeToElement(new { index })
    };
}
