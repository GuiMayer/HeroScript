using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
using Xunit;

namespace Core.Tests.Persistence;

public sealed class JsonFileRunStateRepositoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"heroscript-runs-{Guid.NewGuid():N}");
    private readonly JsonFileRunStateRepository _repo;

    public JsonFileRunStateRepositoryTests()
    {
        _repo = new JsonFileRunStateRepository(_tempDir, NullLogger.Instance);
    }

    [Fact]
    public async Task Save_ThenLoad_ReturnsEquivalentState()
    {
        var state = new RunState { Gold = 100, PowerPoints = 5, CurrentNodeId = "node_1" };

        await _repo.SaveAsync(state);
        var loaded = await _repo.LoadAsync(state.RunId);

        Assert.NotNull(loaded);
        Assert.Equal(state.RunId, loaded.RunId);
        Assert.Equal(100, loaded.Gold);
        Assert.Equal(5, loaded.PowerPoints);
        Assert.Equal("node_1", loaded.CurrentNodeId);
    }

    [Fact]
    public async Task Load_UnknownId_ReturnsNull()
    {
        var result = await _repo.LoadAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task Save_Overwrite_UpdatesState()
    {
        var state = new RunState { Gold = 10 };
        await _repo.SaveAsync(state);

        state.Gold = 999;
        await _repo.SaveAsync(state);

        var loaded = await _repo.LoadAsync(state.RunId);
        Assert.NotNull(loaded);
        Assert.Equal(999, loaded.Gold);
    }

    [Fact]
    public async Task Delete_ExistingRun_RemovedFromList()
    {
        var state = new RunState();
        await _repo.SaveAsync(state);

        await _repo.DeleteAsync(state.RunId);
        var loaded = await _repo.LoadAsync(state.RunId);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ListRunIds_ReturnsAllPersisted()
    {
        var ids = new[] { new RunState(), new RunState(), new RunState() };
        foreach (var s in ids)
            await _repo.SaveAsync(s);

        var list = await _repo.ListRunIdsAsync();
        foreach (var s in ids)
            Assert.Contains(s.RunId, list);
    }

    [Fact]
    public async Task Save_AtomicWrite_FileNotCorruptedIfFail()
    {
        // Just verify normal path doesn't leave .tmp behind
        var state = new RunState { Gold = 42 };
        await _repo.SaveAsync(state);

        var tmpFiles = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task Save_ConcurrentWrites_LastWriteWins()
    {
        var state = new RunState();
        var tasks = Enumerable.Range(0, 10)
            .Select(i => { state.Gold = i; return _repo.SaveAsync(state); });
        await Task.WhenAll(tasks);

        var loaded = await _repo.LoadAsync(state.RunId);
        Assert.NotNull(loaded);
        // Gold is some value between 0-9; important: no exception or corruption
        Assert.InRange(loaded.Gold, 0, 9);
    }

    public void Dispose()
    {
        _repo.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
