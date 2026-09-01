using Core.Caching;
using Xunit;

namespace Core.Tests.Caching;

public sealed class CacheCoordinatorTests
{
    [Fact]
    public void InvalidateAll_UsesDependencyOrder_AndPreservesRevisionedCaches()
    {
        var order = new List<string>();
        var coordinator = new CacheRegistry();
        coordinator.Register(new RecordingCache("runtime", CacheLayer.Runtime, order));
        coordinator.Register(new RecordingCache("source", CacheLayer.Source, order));
        coordinator.Register(new RecordingCache("definitions", CacheLayer.Definition, order));
        coordinator.Register(new RecordingCache("revisioned", CacheLayer.Revisioned, order, preserve: true));

        var report = coordinator.InvalidateAll();

        Assert.Equal(["source", "definitions", "runtime"], order);
        Assert.Equal(3, report.InvalidatedCount);
        Assert.Equal(1, report.PreservedCount);
        Assert.Contains(report.Entries, entry =>
            entry.CacheName == "revisioned" && !entry.Invalidated);
    }

    [Fact]
    public void InvalidateAll_CanExplicitlyIncludeRevisionedCaches()
    {
        var order = new List<string>();
        var coordinator = new CacheRegistry();
        coordinator.Register(new RecordingCache("revisioned", CacheLayer.Revisioned, order, preserve: true));

        var report = coordinator.InvalidateAll(includeRevisioned: true);

        Assert.Equal(["revisioned"], order);
        Assert.Equal(1, report.InvalidatedCount);
    }

    private sealed class RecordingCache(
        string name,
        CacheLayer layer,
        ICollection<string> order,
        bool preserve = false) : ICacheService
    {
        public string CacheName => name;
        public CacheLayer Layer => layer;
        public bool PreserveAcrossGlobalInvalidation => preserve;

        public void Invalidate(string? key = null) => order.Add(name);

        public CacheServiceStats GetStats() => new()
        {
            CacheName = name,
            Capacity = 1
        };
    }
}
