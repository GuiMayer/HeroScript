using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Xunit;

namespace Core.Tests.Events;

/// <summary>
/// Testes de integração do EventBus com outros sistemas.
/// </summary>
public class EventIntegrationTests
{
    [Fact]
    public async Task EventBus_ThreadSafety_MultipleThreadsPublishing()
    {
        // Arrange
        var eventBus = new EventBus(NullLogger.Instance);
        var eventCount = 0;
        var lockObj = new object();

        using var subscription = eventBus.Subscribe<ConfigLoadedEvent>(e =>
        {
            lock (lockObj)
            {
                eventCount++;
            }
        });

        // Act - Publicar eventos de múltiplas threads
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var taskId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                {
                    eventBus.Publish(new ConfigLoadedEvent
                    {
                        ConfigName = $"config-{taskId}-{j}"
                    });
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(100, eventCount);
        Assert.Equal(100, eventBus.GetEventHistory().Count);
    }

    [Fact]
    public async Task EventBus_SubscriptionDisposal_IsThreadSafe()
    {
        // Arrange
        var eventBus = new EventBus(NullLogger.Instance);
        var subscriptions = new List<IDisposable>();

        // Act - Criar e descartar subscriptions de múltiplas threads
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var sub = eventBus.Subscribe<ConfigLoadedEvent>(e => { });
                lock (subscriptions)
                {
                    subscriptions.Add(sub);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Descartar todas as subscriptions
        foreach (var sub in subscriptions)
        {
            sub.Dispose();
        }

        // Assert - Não deve lançar exceção
        eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test" });
    }

    [Fact]
    public void EventBus_HandlerException_DoesNotStopOtherHandlers()
    {
        // Arrange
        var eventBus = new EventBus(NullLogger.Instance);
        var handler1Called = false;
        var handler3Called = false;

        using var sub1 = eventBus.Subscribe<ConfigLoadedEvent>(e => handler1Called = true);
        using var sub2 = eventBus.Subscribe<ConfigLoadedEvent>(e => throw new Exception("Handler error"));
        using var sub3 = eventBus.Subscribe<ConfigLoadedEvent>(e => handler3Called = true);

        // Act
        eventBus.Publish(new ConfigLoadedEvent { ConfigName = "test" });

        // Assert - Handlers 1 e 3 devem ter sido chamados mesmo com erro no handler 2
        Assert.True(handler1Called);
        Assert.True(handler3Called);
    }

    [Fact]
    public void GameEvent_Payload_SupportsComplexTypes()
    {
        // Arrange
        var eventBus = new EventBus(NullLogger.Instance);
        var testEvent = new MathFormulaEvaluatedEvent
        {
            FormulaName = "test",
            InputValue = 10.5f,
            OutputValue = 42.0f,
            Parameters = new Dictionary<string, float>
            {
                { "param1", 1.5f },
                { "param2", 2.5f }
            }
        };

        testEvent.Payload.Add("customData", new { Name = "Test", Value = 123 });
        testEvent.Payload.Add("list", new List<int> { 1, 2, 3 });

        // Act
        eventBus.Publish(testEvent);
        var history = eventBus.GetEventHistory();

        // Assert
        Assert.Single(history);
        var retrieved = history[0] as MathFormulaEvaluatedEvent;
        Assert.NotNull(retrieved);
        Assert.Equal("test", retrieved.FormulaName);
        Assert.Equal(10.5f, retrieved.InputValue);
        Assert.Equal(42.0f, retrieved.OutputValue);
        Assert.Equal(2, retrieved.Parameters.Count);
        Assert.True(retrieved.Payload.ContainsKey("customData"));
        Assert.True(retrieved.Payload.ContainsKey("list"));
    }

    [Fact]
    public void EventBus_SequenceNumbers_AreUniqueAcrossEventTypes()
    {
        // Arrange
        var eventBus = new EventBus(NullLogger.Instance);

        // Act
        eventBus.Publish(new ConfigLoadedEvent { ConfigName = "config1" });
        eventBus.Publish(new MathFormulaEvaluatedEvent { FormulaName = "formula1" });
        eventBus.Publish(new ConfigChangedEvent { OldConfig = "old", NewConfig = "new" });

        var history = eventBus.GetEventHistory();

        // Assert
        var gameEvents = history.Cast<GameEvent>().ToList();
        Assert.Equal(0, gameEvents[0].Sequence);
        Assert.Equal(1, gameEvents[1].Sequence);
        Assert.Equal(2, gameEvents[2].Sequence);
    }

    [Fact]
    public async Task EventBus_ConcurrentPublishing_AssignsUniqueContiguousSequences()
    {
        var eventBus = new EventBus(NullLogger.Instance);

        var tasks = Enumerable.Range(0, 8)
            .Select(worker => Task.Run(() =>
            {
                for (var index = 0; index < 25; index++)
                    eventBus.Publish(new ConfigLoadedEvent { ConfigName = $"config-{worker}-{index}" });
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var sequences = eventBus.GetEventHistory()
            .Cast<GameEvent>()
            .Select(e => e.Sequence)
            .OrderBy(sequence => sequence)
            .ToList();

        Assert.Equal(200, sequences.Count);
        Assert.Equal(Enumerable.Range(0, 200), sequences);
    }
}
