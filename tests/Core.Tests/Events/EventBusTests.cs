using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Infrastructure.Persistence;
using Core.Abstractions.Persistence;
using Xunit;

namespace Core.Tests.Events;

public class EventBusTests
{
    private readonly IEventBus _eventBus;

    public EventBusTests()
    {
        _eventBus = new EventBus(NullLogger.Instance);
    }

    [Fact]
    public void Publish_SingleSubscriber_ReceivesEvent()
    {
        // Arrange
        ConfigLoadedEvent? receivedEvent = null;
        using var subscription = _eventBus.Subscribe<ConfigLoadedEvent>(e => receivedEvent = e);

        var testEvent = new ConfigLoadedEvent
        {
            ConfigName = "test-config",
            ResourcesLoaded = 5
        };

        // Act
        _eventBus.Publish(testEvent);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("test-config", receivedEvent.ConfigName);
        Assert.Equal(5, receivedEvent.ResourcesLoaded);
    }

    [Fact]
    public void Publish_MultipleSubscribers_AllReceiveEvent()
    {
        // Arrange
        int callCount = 0;
        using var sub1 = _eventBus.Subscribe<ConfigLoadedEvent>(e => callCount++);
        using var sub2 = _eventBus.Subscribe<ConfigLoadedEvent>(e => callCount++);
        using var sub3 = _eventBus.Subscribe<ConfigLoadedEvent>(e => callCount++);

        // Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test" });

        // Assert
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void Subscribe_Dispose_StopsReceivingEvents()
    {
        // Arrange
        int callCount = 0;
        var subscription = _eventBus.Subscribe<ConfigLoadedEvent>(e => callCount++);

        // Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test1" });
        subscription.Dispose();
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test2" });

        // Assert
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetEventHistory_ReturnsAllPublishedEvents()
    {
        // Arrange & Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config2" });
        _eventBus.Publish(new MathFormulaEvaluatedEvent { FormulaName = "formula1" });

        var history = _eventBus.GetEventHistory();

        // Assert
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void GetEventHistory_FilterByCategory_ReturnsMatchingEvents()
    {
        // Arrange
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" }); // CONFIG
        _eventBus.Publish(new MathFormulaEvaluatedEvent { FormulaName = "formula1" }); // META
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config2" }); // CONFIG

        // Act
        var configEvents = _eventBus.GetEventHistory(EventCategory.CONFIG);

        // Assert
        Assert.Equal(2, configEvents.Count);
        Assert.All(configEvents.Cast<GameEvent>(), e => Assert.Equal(EventCategory.CONFIG, e.Category));
    }

    [Fact]
    public void GetEventHistory_FilterBySeverity_ReturnsMatchingEvents()
    {
        // Arrange
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" }); // INFO
        _eventBus.Publish(new MathFormulaEvaluatedEvent { FormulaName = "formula1" }); // DEBUG
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config2" }); // INFO

        // Act
        var infoEvents = _eventBus.GetEventHistory(EventSeverity.INFO);

        // Assert
        Assert.Equal(2, infoEvents.Count);
        Assert.All(infoEvents.Cast<GameEvent>(), e => Assert.Equal(EventSeverity.INFO, e.Severity));
    }

    [Fact]
    public void Publish_DifferentEventTypes_OnlyMatchingSubscribersReceive()
    {
        // Arrange
        int configCount = 0;
        int mathCount = 0;

        using var configSub = _eventBus.Subscribe<ConfigLoadedEvent>(e => configCount++);
        using var mathSub = _eventBus.Subscribe<MathFormulaEvaluatedEvent>(e => mathCount++);

        // Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test" });
        _eventBus.Publish(new MathFormulaEvaluatedEvent { FormulaName = "test" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test2" });

        // Assert
        Assert.Equal(2, configCount);
        Assert.Equal(1, mathCount);
    }

    [Fact]
    public void ClearHistory_RemovesAllEvents()
    {
        // Arrange
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test1" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test2" });

        // Act
        _eventBus.ClearHistory();
        var history = _eventBus.GetEventHistory();

        // Assert
        Assert.Empty(history);
    }

    [Fact]
    public void Publish_AssignsSequenceNumbers()
    {
        // Arrange & Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config2" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config3" });

        var history = _eventBus.GetEventHistory();

        // Assert
        var gameEvents = history.Cast<GameEvent>().ToList();
        Assert.Equal(0, gameEvents[0].Sequence);
        Assert.Equal(1, gameEvents[1].Sequence);
        Assert.Equal(2, gameEvents[2].Sequence);
    }

    [Fact]
    public void Publish_SubscribersReceiveSequencedHistoryEvent()
    {
        ConfigLoadedEvent? receivedEvent = null;
        using var subscription = _eventBus.Subscribe<ConfigLoadedEvent>(e => receivedEvent = e);

        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });

        var historyEvent = Assert.IsType<ConfigLoadedEvent>(Assert.Single(_eventBus.GetEventHistory()));
        Assert.NotNull(receivedEvent);
        Assert.Equal(historyEvent.Sequence, receivedEvent.Sequence);
        Assert.Equal(historyEvent.EventId, receivedEvent.EventId);
    }

    [Fact]
    public void Publish_FirstHandlerException_DoesNotStopOtherHandlersOrHistory()
    {
        var secondHandlerCalled = false;
        using var failing = _eventBus.Subscribe<ConfigLoadedEvent>(_ => throw new InvalidOperationException("handler failed"));
        using var succeeding = _eventBus.Subscribe<ConfigLoadedEvent>(_ => secondHandlerCalled = true);

        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });

        Assert.True(secondHandlerCalled);
        Assert.Single(_eventBus.GetEventHistory());
    }

    [Fact]
    public void Publish_PayloadCannotBeMutatedByHandlers()
    {
        var source = new Dictionary<string, object> { ["value"] = 1 };
        var published = new ConfigLoadedEvent { ConfigName = "config1", Payload = source };

        source["value"] = 2;
        _eventBus.Publish(published);

        var historyEvent = Assert.IsType<ConfigLoadedEvent>(Assert.Single(_eventBus.GetEventHistory()));
        Assert.Equal(1, historyEvent.Payload["value"]);
    }

    [Fact]
    public void GetEventHistory_MutatingReturnedList_DoesNotAffectStoredHistory()
    {
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });

        var returnedHistory = _eventBus.GetEventHistory();
        var mutableCopy = Assert.IsType<List<IEvent>>(returnedHistory);
        mutableCopy.Clear();

        Assert.Single(_eventBus.GetEventHistory());
    }

    [Fact]
    public void Publish_SetsTimestamp()
    {
        // Arrange
        // Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test" });
        var history = _eventBus.GetEventHistory();

        // Assert
        Assert.Single(history);
        var @event = history[0];
        Assert.Equal(DateTime.UnixEpoch, @event.Timestamp);
    }

    [Fact]
    public void Publish_GeneratesUniqueEventIds()
    {
        // Arrange & Act
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config2" });
        _eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config3" });

        var history = _eventBus.GetEventHistory();

        // Assert
        var eventIds = history.Select(e => e.EventId).ToList();
        Assert.Equal(3, eventIds.Distinct().Count());
    }

    [Fact]
    public void Publish_SameEventStream_ReproducesIdentityAndLogicalTime()
    {
        var first = new EventBus(NullLogger.Instance);
        var second = new EventBus(NullLogger.Instance);

        first.Publish(new ConfigLoadedEvent { ConfigName = "config1" });
        first.Publish(new ConfigLoadedEvent { ConfigName = "config2" });
        second.Publish(new ConfigLoadedEvent { ConfigName = "config1" });
        second.Publish(new ConfigLoadedEvent { ConfigName = "config2" });

        var firstHistory = first.GetEventHistory();
        var secondHistory = second.GetEventHistory();
        Assert.Equal(
            firstHistory.Select(item => (item.EventId, item.Timestamp)),
            secondHistory.Select(item => (item.EventId, item.Timestamp)));
    }

    [Fact]
    public void Subscribe_WithNullHandler_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _eventBus.Subscribe<ConfigLoadedEvent>(null!));
    }

    [Fact]
    public void Publish_WithNullEvent_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _eventBus.Publish<ConfigLoadedEvent>(null!));
    }

    [Fact]
    public void Publish_EnrichesEveryGameEventFromAmbientAndEventMetadata()
    {
        var accessor = new GameEventContextAccessor();
        var eventBus = new EventBus(NullLogger.Instance, contextAccessor: accessor);
        var runId = Guid.NewGuid();
        var combatId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        using (accessor.Push(new GameEventContext
        {
            TraceId = "request-42",
            RunId = runId,
            CommandId = commandId,
            CorrelationId = commandId
        }))
        {
            eventBus.Publish(new CombatStartedEvent { CombatId = combatId });
        }

        var published = Assert.IsType<CombatStartedEvent>(Assert.Single(eventBus.GetEventHistory()));
        Assert.Equal("request-42", published.Context.TraceId);
        Assert.Equal(runId, published.Context.RunId);
        Assert.Equal(combatId, published.Context.CombatId);
        Assert.Equal(commandId, published.Context.CommandId);
        Assert.Equal(commandId, published.CorrelationId);
    }

    [Fact]
    public async Task Publish_IsDurableBeforeReturning_AndSequenceContinuesAfterRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"heroscript-bus-{Guid.NewGuid():N}");
        try
        {
            using var store = new JsonFileEventStore(directory, NullLogger.Instance);
            var first = new EventBus(NullLogger.Instance, store);
            first.Publish(new ConfigLoadedEvent { ConfigName = "first" });

            Assert.Single(await store.GetEventsAsync(new EventStoreFilter()));

            var restarted = new EventBus(NullLogger.Instance, store);
            restarted.Publish(new ConfigLoadedEvent { ConfigName = "second" });
            var events = await store.GetEventsAsync(new EventStoreFilter(Limit: 10));

            Assert.Equal(new[] { 0, 1 }, events.Cast<GameEvent>().Select(item => item.Sequence));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
