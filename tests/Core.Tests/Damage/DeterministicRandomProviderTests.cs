using Core.Damage;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Damage;

public class DeterministicRandomProviderTests
{
    [Fact]
    public void SameContext_ProducesSameSequenceAndFinalState()
    {
        var context = DeterministicContext.Create(42UL, "test-content");
        var first = new DeterministicRandomProvider(context);
        var second = new DeterministicRandomProvider(context);

        var firstValues = new[]
        {
            first.NextDouble(),
            first.Next(100),
            first.Next(10, 20)
        };
        var secondValues = new[]
        {
            second.NextDouble(),
            second.Next(100),
            second.Next(10, 20)
        };

        Assert.Equal(firstValues, secondValues);
        Assert.Equal(first.Context, second.Context);
        Assert.Equal(3UL, first.Context.RandomState.DrawCount);
    }

    [Fact]
    public void AllocateId_AdvancesOnlyIdSequence()
    {
        var context = DeterministicContext.Create(7UL, "test-content");
        var provider = new DeterministicRandomProvider(context);

        var id = provider.AllocateId("effect");

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(1UL, provider.Context.IdSequence);
        Assert.Equal(0UL, provider.Context.RandomState.DrawCount);
        Assert.Equal(context.Step, provider.Context.Step);
    }
}
