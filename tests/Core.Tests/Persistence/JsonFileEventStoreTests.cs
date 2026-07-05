using Core.Abstractions.Persistence;
using Core.Events;
using Core.Infrastructure.Persistence;
using Core.Logging;
using Xunit;

namespace Core.Tests.Persistence;

public sealed class JsonFileEventStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"heroscript-events-{Guid.NewGuid():N}");
    private readonly JsonFileEventStore _store;

    public JsonFileEventStoreTests()
    {
        _store = new JsonFileEventStore(_tempDir, NullLogger.Instance);
    }

    [Fact]
    public async Task Append_ThenGet_ReturnsEvent()
    {
        var evt = new TestGameEvent("TestType", 1);
        await _store.AppendAsync(evt);

        var results = await _store.GetEventsAsync(new EventStoreFilter());
        Assert.Single(results);
        Assert.Equal("TestType", results[0].EventType);
    }

    [Fact]
    public async Task Append_MultipleEvents_PreservesAll()
    {
        for (int i = 0; i < 5; i++)
            await _store.AppendAsync(new TestGameEvent($"Event{i}", i));

        var results = await _store.GetEventsAsync(new EventStoreFilter(Limit: 100));
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task GetEvents_EmptyFile_ReturnsEmpty()
    {
        var results = await _store.GetEventsAsync(new EventStoreFilter());
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetEvents_AfterSequenceFilter_FiltersCorrectly()
    {
        await _store.AppendAsync(new TestGameEvent("A", sequence: 0));
        await _store.AppendAsync(new TestGameEvent("B", sequence: 1));
        await _store.AppendAsync(new TestGameEvent("C", sequence: 2));

        var results = await _store.GetEventsAsync(new EventStoreFilter(AfterSequence: 0));
        // Events with sequence > 0 pass the filter
        Assert.DoesNotContain(results, e => e is GameEvent g && g.Sequence <= 0);
    }

    [Fact]
    public async Task GetEvents_TypeFilter_FiltersCorrectly()
    {
        await _store.AppendAsync(new TestGameEvent("TypeA", 0));
        await _store.AppendAsync(new TestGameEvent("TypeB", 1));

        var results = await _store.GetEventsAsync(new EventStoreFilter(EventType: "TypeA"));
        Assert.All(results, e => Assert.Equal("TypeA", e.EventType));
    }

    [Fact]
    public async Task GetEvents_LimitRespected()
    {
        for (int i = 0; i < 10; i++)
            await _store.AppendAsync(new TestGameEvent("Evt", i));

        var results = await _store.GetEventsAsync(new EventStoreFilter(Limit: 3));
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Append_ConcurrentWrites_NoDataLoss()
    {
        var tasks = Enumerable.Range(0, 20)
            .Select(i => _store.AppendAsync(new TestGameEvent($"ConcurrentEvent{i}", i)));
        await Task.WhenAll(tasks);

        var results = await _store.GetEventsAsync(new EventStoreFilter(Limit: 100));
        Assert.Equal(20, results.Count);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed record TestGameEvent : GameEvent
    {
        public TestGameEvent(string eventType, int sequence)
        {
            EventType = eventType;
            Sequence = sequence;
            Category = EventCategory.GAME;
            Severity = EventSeverity.INFO;
        }
    }
}
