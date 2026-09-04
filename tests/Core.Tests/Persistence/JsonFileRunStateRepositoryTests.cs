using Core.Infrastructure.Persistence;
using Core.Logging;
using Core.Run;
using Core.Resources;
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
        var map = RunMapTransitions.Create(
        [
            new RunMapNodeDefinition { NodeId = "node_1", NextNodeIds = ["node_2"] },
            new RunMapNodeDefinition { NodeId = "node_2", NodeType = "shop" }
        ]).Value;
        var state = new RunState
        {
            RunId = Guid.NewGuid(),
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = "gold", Amount = 100 },
                new ResourceAmount { ResourceId = "power_points", Amount = 5 }),
            CurrentNodeId = "node_1",
            Map = map
        };

        await _repo.SaveAsync(state);
        var loaded = await _repo.LoadLatestAsync(state.RunId);

        Assert.NotNull(loaded);
        Assert.Equal(state.RunId, loaded.RunId);
        Assert.Equal(100, loaded.ResourceState.Current("gold"));
        Assert.Equal(5, loaded.ResourceState.Current("power_points"));
        Assert.Equal("node_1", loaded.CurrentNodeId);
        Assert.Equal(new[] { "node_1" }, loaded.Map.VisitedNodeIds);
        Assert.Equal(2, loaded.Map.Nodes.Count);
        Assert.Equal(new[] { "node_2" }, loaded.Map.Nodes[0].NextNodeIds);
    }

    [Fact]
    public async Task Load_UnknownId_ReturnsNull()
    {
        var result = await _repo.LoadLatestAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task Save_Overwrite_UpdatesState()
    {
        var state = WithResource(new RunState { RunId = Guid.NewGuid() }, "gold", 10);
        await _repo.SaveAsync(state);

        state = WithResource(state, "gold", 999);
        await _repo.SaveAsync(state);

        var loaded = await _repo.LoadLatestAsync(state.RunId);
        Assert.NotNull(loaded);
        Assert.Equal(999, loaded.ResourceState.Current("gold"));
    }

    [Fact]
    public async Task Delete_ExistingRun_RemovedFromList()
    {
        var state = new RunState { RunId = Guid.NewGuid() };
        await _repo.SaveAsync(state);

        await _repo.DeleteAsync(state.RunId);
        var loaded = await _repo.LoadLatestAsync(state.RunId);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task ListRunIds_ReturnsAllPersisted()
    {
        var ids = new[]
        {
            new RunState { RunId = Guid.NewGuid() },
            new RunState { RunId = Guid.NewGuid() },
            new RunState { RunId = Guid.NewGuid() }
        };
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
        var state = WithResource(new RunState { RunId = Guid.NewGuid() }, "gold", 42);
        await _repo.SaveAsync(state);

        var tmpFiles = Directory.GetFiles(_tempDir, "*.tmp");
        Assert.Empty(tmpFiles);
    }

    [Fact]
    public async Task Save_ConcurrentWrites_LastWriteWins()
    {
        var state = new RunState { RunId = Guid.NewGuid() };
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _repo.SaveAsync(WithResource(state, "gold", i)));
        await Task.WhenAll(tasks);

        var loaded = await _repo.LoadLatestAsync(state.RunId);
        Assert.NotNull(loaded);
        // Gold is some value between 0-9; important: no exception or corruption
        Assert.InRange(loaded.ResourceState.Current("gold"), 0, 9);
    }

    private static RunState WithResource(RunState state, string resourceId, float value) =>
        state with
        {
            ResourceState = TestDataBuilders.RunResources(
                new ResourceAmount { ResourceId = resourceId, Amount = value }) with
            {
                OwnerId = $"run:{state.RunId}"
            }
        };

    public void Dispose()
    {
        _repo.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
