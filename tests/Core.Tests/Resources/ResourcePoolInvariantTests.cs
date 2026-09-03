using Core.Resources;
using Xunit;

namespace Core.Tests.Resources;

public sealed class ResourcePoolInvariantTests
{
    [Fact]
    public void Spend_RespectsNonZeroMinimum()
    {
        var pool = Pool(current: 5, minimum: 3, maximum: 10);

        Assert.False(pool.CanAfford(3));
        Assert.Throws<InvalidOperationException>(() => pool.Spend(3));
        Assert.Equal(3, pool.Spend(2).Current);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Mutations_RejectInvalidAmounts(float value)
    {
        var pool = Pool(current: 5, minimum: 0, maximum: 10);

        Assert.False(pool.CanAfford(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Spend(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Gain(value));
    }

    [Fact]
    public void Percentage_NormalizesAgainstMinimum()
    {
        var pool = Pool(current: 15, minimum: 10, maximum: 20);

        Assert.Equal(50, pool.GetPercentage());
    }

    private static ResourcePool Pool(float current, float minimum, float maximum)
    {
        var definition = new ResourceDefinition
        {
            ResourceId = "test",
            DisplayName = "Test",
            DefaultCurrent = current,
            DefaultMin = minimum,
            DefaultMax = maximum
        };
        return new ResourcePool
        {
            ResourceId = definition.ResourceId,
            Definition = definition,
            Current = current,
            Minimum = minimum,
            Maximum = maximum
        };
    }
}
