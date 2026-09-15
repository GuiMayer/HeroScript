using Core.CardZones;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneSystemCompilerTests
{
    [Fact]
    public void Compile_IndexesBoundariesInStablePriorityOrder()
    {
        var result = CardZoneSystemCompiler.Compile(System(
            Flow("later", 20),
            Flow("earlier", 10)));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["earlier", "later"], result.Value.FlowsByTrigger["activation.started"].Select(flow => flow.FlowId));
    }

    [Fact]
    public void Compile_RejectsRecursiveFallbacks()
    {
        var first = Flow("first", 0) with
        {
            Steps = [Move("one") with { OnInsufficient = CardZoneInsufficientPolicy.ExecuteFallbackAndRetry, FallbackFlowId = "second" }]
        };
        var second = Flow("second", 0) with
        {
            Steps = [Move("two") with { OnInsufficient = CardZoneInsufficientPolicy.ExecuteFallbackAndRetry, FallbackFlowId = "first" }]
        };

        var result = CardZoneSystemCompiler.Compile(System(first, second));

        Assert.True(result.IsFailure);
        Assert.Contains("cycle", result.Error);
    }

    private static CardZoneSystemDefinition System(params CardZoneFlowDefinition[] flows) => new()
    {
        CardZoneSystemId = "test-zones",
        Zones =
        [
            new() { ZoneId = "library", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered },
            new() { ZoneId = "ready", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered }
        ],
        Flows = flows
    };

    private static CardZoneFlowDefinition Flow(string id, int priority) => new()
    {
        FlowId = id,
        Priority = priority,
        Triggers = ["activation.started"],
        AllowedInvocations = [CardZoneFlowInvocation.Boundary],
        Steps = [Move("move")]
    };

    private static CardZoneFlowStepDefinition Move(string id) => new()
    {
        StepId = id,
        Operation = CardZoneOperation.Move,
        SourceZoneId = "library",
        TargetZoneId = "ready",
        Selection = new() { Strategy = CardZoneSelectionStrategy.Top, Count = 1 }
    };
}
