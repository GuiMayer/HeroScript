using Core.Diagnostics;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Diagnostics;

public sealed class OperationalTimingTests
{
    [Fact]
    public void MeasurementsNeverChangeCanonicalOutputAndNestedScopesAreIsolated()
    {
        var value = new { Seed = 123, Value = "immutable" };
        var before = CanonicalJson.ComputeHash(value);
        using var outer = OperationalTiming.Begin();
        Assert.Equal(before, CanonicalJson.ComputeHash(value));
        using (var inner = OperationalTiming.Begin())
        {
            using (OperationalTiming.Measure("inner")) { }
            Assert.True(inner.Snapshot().ContainsKey("inner"));
        }
        Assert.False(outer.Snapshot().ContainsKey("inner"));
        Assert.True(outer.Snapshot().ContainsKey("canonical_hash"));
        Assert.Equal(before, CanonicalJson.ComputeHash(value));
    }
}
