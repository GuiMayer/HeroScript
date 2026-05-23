using API.Controllers;
using Core.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests.Controllers;

public sealed class EventsControllerTests
{
    private readonly RecordingEventBus _eventBus = new();
    private readonly EventsController _controller;

    public EventsControllerTests()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns("Development");
        _controller = new EventsController(_eventBus, Mock.Of<ILogger<EventsController>>(), environment.Object);
    }

    [Fact]
    public void GetEvents_FiltersBySequenceCombatRunAndType()
    {
        var combatId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _eventBus.Events.Add(CreateEvent(0, "IgnoredEvent", combatId, runId));
        _eventBus.Events.Add(CreateEvent(1, "ActivationStartedEvent", combatId, runId));
        _eventBus.Events.Add(CreateEvent(2, "ActivationStartedEvent", Guid.NewGuid(), runId));
        _eventBus.Events.Add(CreateEvent(3, "ActivationStartedEvent", combatId, Guid.NewGuid()));
        _eventBus.Events.Add(CreateEvent(4, "ActivationEndedEvent", combatId, runId));

        var result = _controller.GetEvents(
            limit: 10,
            afterSequence: 0,
            combatId: combatId,
            runId: runId,
            eventType: "ActivationStartedEvent");

        var ok = Assert.IsType<OkObjectResult>(result);
        var text = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"returned\":1", text);
        Assert.Contains("ActivationStartedEvent", text);
        Assert.DoesNotContain("ActivationEndedEvent", text);
    }

    [Fact]
    public void GetCombatEvents_AppliesCombatFilter()
    {
        var combatId = Guid.NewGuid();
        var otherCombatId = Guid.NewGuid();
        _eventBus.Events.Add(CreateEvent(1, "ActivationStartedEvent", combatId, Guid.NewGuid()));
        _eventBus.Events.Add(CreateEvent(2, "ActivationStartedEvent", otherCombatId, Guid.NewGuid()));

        var result = _controller.GetCombatEvents(combatId, limit: 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var text = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"total\":1", text);
        Assert.Contains("\"returned\":1", text);
        Assert.DoesNotContain(otherCombatId.ToString(), text);
    }

    private static GameEvent CreateEvent(int sequence, string eventType, Guid combatId, Guid runId) => new()
    {
        EventType = eventType,
        Sequence = sequence,
        Category = EventCategory.COMBAT,
        Severity = EventSeverity.INFO,
        Payload = new Dictionary<string, object>
        {
            ["combatId"] = combatId.ToString(),
            ["runId"] = runId.ToString()
        }
    };

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IEvent> Events { get; } = new();

        public void Publish<TEvent>(TEvent @event) where TEvent : IEvent => Events.Add(@event);
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IEvent => new NoopDisposable();
        public IReadOnlyList<IEvent> GetEventHistory() => Events;
        public IReadOnlyList<IEvent> GetEventHistory(EventCategory category) => Events.OfType<GameEvent>().Where(e => e.Category == category).ToList();
        public IReadOnlyList<IEvent> GetEventHistory(EventSeverity severity) => Events.OfType<GameEvent>().Where(e => e.Severity == severity).ToList();
        public void ClearHistory() => Events.Clear();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
