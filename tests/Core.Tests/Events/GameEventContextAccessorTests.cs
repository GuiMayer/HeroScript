using Core.Events;
using Xunit;

namespace Core.Tests.Events;

public sealed class GameEventContextAccessorTests
{
    [Fact]
    public void NestedScopes_MergeAndRestoreContext()
    {
        var accessor = new GameEventContextAccessor();
        var runId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        using (accessor.Push(new GameEventContext { TraceId = "trace", RunId = runId }))
        {
            using (accessor.Push(new GameEventContext
            {
                CommandId = commandId,
                CorrelationId = commandId
            }))
            {
                Assert.Equal("trace", accessor.Current.TraceId);
                Assert.Equal(runId, accessor.Current.RunId);
                Assert.Equal(commandId, accessor.Current.CommandId);
            }

            Assert.Null(accessor.Current.CommandId);
            Assert.Equal(runId, accessor.Current.RunId);
        }

        Assert.True(accessor.Current.IsEmpty);
    }

    [Fact]
    public async Task ConcurrentFlows_DoNotLeakContext()
    {
        var accessor = new GameEventContextAccessor();
        var tasks = Enumerable.Range(0, 12).Select(async index =>
        {
            var traceId = $"trace-{index}";
            using (accessor.Push(new GameEventContext { TraceId = traceId }))
            {
                await Task.Yield();
                Assert.Equal(traceId, accessor.Current.TraceId);
            }
        });

        await Task.WhenAll(tasks);
        Assert.True(accessor.Current.IsEmpty);
    }
}
