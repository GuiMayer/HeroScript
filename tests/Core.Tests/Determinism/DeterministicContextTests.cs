using System.Text.Json;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Determinism;

[Trait("Category", "Unit")]
public class DeterministicContextTests
{
    [Fact]
    public void AllocateId_IsStableAndAdvancesOnlyIdSequence()
    {
        var context = DeterministicContext.Create(99, "cards-sha256");

        var first = context.AllocateId("card-instance");
        var repeated = context.AllocateId("card-instance");
        var second = first.Context.AllocateId("card-instance");

        Assert.Equal(first.Value, repeated.Value);
        Assert.NotEqual(first.Value, second.Value);
        Assert.Equal(0UL, context.IdSequence);
        Assert.Equal(1UL, first.Context.IdSequence);
        Assert.Equal(context.RandomState, first.Context.RandomState);
        Assert.Equal('8', first.Value.ToString("D")[14]);
    }

    [Fact]
    public void AdvanceStep_UsesLogicalClockInsteadOfWallClock()
    {
        var context = DeterministicContext.Create(7, "content-v2");

        var advanced = context.AdvanceStep(logicalTicks: 25);

        Assert.Equal(1UL, advanced.Step);
        Assert.Equal(25, advanced.LogicalTick);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(25), advanced.LogicalTimestamp);
        Assert.Equal(0UL, context.Step);
    }

    [Fact]
    public void Context_RoundTripsWithoutLosingReplayState()
    {
        var original = DeterministicContext.Create(55, "content-v3")
            .DrawUInt64().Context
            .AllocateId("event").Context
            .AdvanceStep(10);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DeterministicContext>(json);

        Assert.Equal(original, restored);
    }
}
