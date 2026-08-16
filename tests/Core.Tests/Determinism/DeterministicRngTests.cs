using Core.Determinism;
using Xunit;

namespace Core.Tests.Determinism;

[Trait("Category", "Unit")]
public class DeterministicRngTests
{
    [Fact]
    public void NextUInt64_UsesStableSplitMix64Sequence()
    {
        var first = DeterministicRng.NextUInt64(DeterministicRngState.FromSeed(0));
        var second = DeterministicRng.NextUInt64(first.NextState);

        Assert.Equal(0xE220A8397B1DCDAFUL, first.Value);
        Assert.Equal(0x6E789E6AA1B965F4UL, second.Value);
        Assert.Equal(2UL, second.NextState.DrawCount);
    }

    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var left = DeterministicContext.Create(42, "content-v1");
        var right = DeterministicContext.Create(42, "content-v1");

        for (var index = 0; index < 100; index++)
        {
            var leftDraw = left.DrawInt32(17);
            var rightDraw = right.DrawInt32(17);
            Assert.Equal(leftDraw.Value, rightDraw.Value);
            left = leftDraw.Context;
            right = rightDraw.Context;
        }

        Assert.Equal(left, right);
    }

    [Fact]
    public void Drawing_DoesNotMutateOriginalContext()
    {
        var original = DeterministicContext.Create(123, "content-v1");

        var draw = original.DrawUInt64();

        Assert.Equal(0UL, original.RandomState.DrawCount);
        Assert.Equal(1UL, draw.Context.RandomState.DrawCount);
        Assert.NotEqual(original, draw.Context);
    }

    [Fact]
    public void NextInt32_RejectsInvalidUpperBound()
    {
        var state = DeterministicRngState.FromSeed(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicRng.NextInt32(state, 0));
    }
}
