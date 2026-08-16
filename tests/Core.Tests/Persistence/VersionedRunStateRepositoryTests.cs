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

        Assert.Equal(new[] { 2, 3 }, snapshots);
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
