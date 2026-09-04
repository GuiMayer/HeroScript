using Core.Infrastructure.Persistence;
using Core.Abstractions.Persistence;
using Core.Determinism;
using System.Text.Json;
using Core.Logging;
using Core.Run;
using Core.Resources;
using Xunit;

namespace Core.Tests.Persistence;

public sealed class VersionedRunStateRepositoryTests : IDisposable
{
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"heroscript-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_WhenRetentionIsEnabled_CompletesAndRetainsNewestSnapshots()
    {
        using var repository = new VersionedRunStateRepository(_storePath, new TestLogger(), maxRetainedSnapshots: 2);
        var runId = Guid.NewGuid();

        for (var sequence = 1; sequence <= 3; sequence++)
        {
            await repository.SaveAsync(new RunState { RunId = runId, Sequence = sequence });
        }

        var snapshots = await repository.ListSnapshotsAsync(runId);
        var journal = await repository.LoadJournalAsync(runId, limit: 10);
        var checkpoints = await repository.LoadCheckpointsAsync(runId);

        Assert.Equal(new[] { 2, 3 }, snapshots);
        Assert.Equal(new[] { 1, 2, 3 }, journal.Select(entry => entry.Sequence));
        Assert.Equal(new[] { 1, 2, 3 }, checkpoints.Select(entry => entry.State.Sequence));
    }

    [Fact]
    public async Task SaveCheckpointBatch_PublishesEveryCheckpointAtomically()
    {
        using var repository = new VersionedRunStateRepository(_storePath, new TestLogger());
        var runId = Guid.NewGuid();
        var firstState = new RunState
        {
            RunId = runId,
            Sequence = 1,
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 10 })
        };
        var firstHash = CanonicalJson.ComputeHash(firstState);
        var secondState = firstState with
        {
            Sequence = 2,
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "credits", Amount = 20 })
        };
        var secondHash = CanonicalJson.ComputeHash(secondState);
        var commandId = Guid.NewGuid();
        var batch = new RunCheckpointBatch
        {
            RunId = runId,
            RootCommandId = commandId,
            Checkpoints =
            [
                new RunCheckpoint(firstState, new RunJournalEntry
                {
                    RunId = runId,
                    CommandId = commandId,
                    Sequence = 1,
                    CommandType = "ROOT",
                    Command = JsonSerializer.SerializeToElement(new { }),
                    StateHash = firstHash
                }),
                new RunCheckpoint(secondState, new RunJournalEntry
                {
                    RunId = runId,
                    CommandId = commandId,
                    Sequence = 2,
                    CommandType = "INTERNAL",
                    Command = JsonSerializer.SerializeToElement(new { }),
                    PreviousStateHash = firstHash,
                    StateHash = secondHash
                })
            ]
        };

        await repository.SaveCheckpointBatchAsync(batch);

        var loaded = await repository.LoadCheckpointsAsync(runId);
        Assert.Equal([1, 2], loaded.Select(checkpoint => checkpoint.State.Sequence));
        Assert.Equal(20, (await repository.LoadLatestAsync(runId))?.ResourceState.Current("credits"));
        Assert.Single(Directory.GetFiles(Path.Combine(_storePath, runId.ToString(), "batches"), "*.json"));
    }

    [Fact]
    public async Task SaveCheckpointBatch_RejectsBrokenHashChainWithoutWritingAnything()
    {
        using var repository = new VersionedRunStateRepository(_storePath, new TestLogger());
        var runId = Guid.NewGuid();
        var state = new RunState { RunId = runId, Sequence = 1 };
        var batch = new RunCheckpointBatch
        {
            RunId = runId,
            Checkpoints =
            [
                new RunCheckpoint(state, new RunJournalEntry
                {
                    RunId = runId,
                    Sequence = 1,
                    Command = JsonSerializer.SerializeToElement(new { }),
                    StateHash = "incorrect"
                })
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.SaveCheckpointBatchAsync(batch));

        Assert.Null(await repository.LoadLatestAsync(runId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_storePath))
            Directory.Delete(_storePath, recursive: true);
    }

    private sealed class TestLogger : ILogger
    {
        public void LogDebug(string message) { }
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message) { }
        public void LogError(string message, Exception exception) { }
    }
}
