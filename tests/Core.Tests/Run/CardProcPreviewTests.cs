using Core.Effects;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardProcPreviewTests
{
    [Fact]
    public void ProjectionGroupsOneCondensationAndRetainsConsumptionAndCausalHops()
    {
        EffectExecutionStep[] steps =
        [
            new() { Identity = new() { ProcId = "one", ImpactId = "a" }, TargetEntityId = "first",
                Condensation = new(), Applications = [new() { StackChanges = [new()
                { Reason = EffectStackChangeReason.Consume, DefinitionId = "charges", PreviousStacks = 10000, CurrentStacks = 0 }] }] },
            new() { Identity = new() { ProcId = "one", ImpactId = "b" }, TargetEntityId = "next",
                Continuation = new() { FromEntityId = "first", ToEntityId = "next" } },
            new() { Identity = new() { ProcId = "child", ParentProcId = "one", ImpactId = "c" }, TargetEntityId = "next" }
        ];
        var result = CardProcPreviewProjector.Project(steps);
        Assert.Equal(2, result.Length);
        Assert.True(result[0].IsCondensation);
        Assert.Equal(new[] { "a", "b" }, result[0].ImpactIds);
        Assert.Equal(new[] { "first", "next" }, result[0].TargetEntityIds);
        Assert.Equal(10000, Assert.Single(result[0].ConsumedStacks).PreviousStacks);
        Assert.Equal("next", Assert.Single(result[0].Continuations).ToEntityId);
        Assert.Equal("one", result[1].ParentProcId);
        Assert.Empty(result[1].ConsumedStacks);
    }
}
