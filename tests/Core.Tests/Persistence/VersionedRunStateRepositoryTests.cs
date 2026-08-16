using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
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
