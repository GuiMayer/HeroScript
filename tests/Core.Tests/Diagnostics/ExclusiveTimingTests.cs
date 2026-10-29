using Core.Diagnostics;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Diagnostics;

public sealed class ExclusiveTimingTests
{
    [Fact]
    public void NestedSameNameCountsCallsAndDoesNotChargeChildTimeAsExclusive()
    {
        using var scope = OperationalTiming.Begin();
        using (OperationalTiming.Measure("root"))
        {
            using (OperationalTiming.Measure("root"))
                CanonicalJson.ComputeHash(new { value = 3 });
        }
        var totals = scope.Details()["root"];
        Assert.Equal(2, totals.Calls);
        Assert.True(totals.InclusiveMs >= totals.ExclusiveMs);
        Assert.True(scope.Details().ContainsKey("canonical_digest"));
        Assert.True(scope.Counters()["hash_digest_bytes"] > 0);
        var copy = scope.Counters();
        OperationalTiming.Count("later");
        Assert.False(copy.ContainsKey("later"));
    }

    [Fact]
    public void CountersAreBoundedAndDisabledDiagnosticsNeverChangeHashes()
    {
        var expected = CanonicalJson.ComputeHash(new { value = 3 });
        OperationalTiming.Count("outside");
        using var scope = OperationalTiming.Begin();
        for (var i = 0; i < 200; i++) OperationalTiming.Count("counter_" + i);
        Assert.Equal(128, scope.Counters().Count);
        Assert.Equal(expected, CanonicalJson.ComputeHash(new { value = 3 }));
        Assert.False(scope.Counters().ContainsKey("outside"));
    }
}
