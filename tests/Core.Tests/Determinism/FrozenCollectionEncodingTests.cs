using System.Collections.Immutable;
using System.Text;
using Core.Determinism;
using Core.Combat.Models;
using Xunit;

namespace Core.Tests.Determinism;

public sealed class FrozenCollectionEncodingTests
{
    [Fact]
    public void BoxedFrozenArraysCompareByStorageAndStableArraysAreNotReencoded()
    {
        var order = Enumerable.Range(0, 1024).Select(index => $"actor-{index}").ToImmutableArray();
        object firstBox = order, secondBox = order;
        Assert.NotSame(firstBox, secondBox);
        Assert.True(firstBox.Equals(secondBox));
        Assert.Equal(firstBox.GetHashCode(), secondBox.GetHashCode());
        Assert.False(firstBox.Equals(order.ToArray().ToImmutableArray()));
        var combat = new CombatState { ActorOrder = order };
        using var metrics = Core.Diagnostics.OperationalTiming.Begin();
        using var scope = CanonicalJson.BeginHashScope();
        long referenceBytes = 0;
        for (var turn = 0; turn < 20; turn++)
        {
            combat = combat with { CurrentTurn = turn };
            var reference = CanonicalJson.Serialize(combat);
            referenceBytes += Encoding.UTF8.GetByteCount(reference);
            Assert.Equal(reference, Encoding.UTF8.GetString(CanonicalJson.Property("combat", combat).Bytes.Span));
        }
        Assert.True(metrics.Counters()["immutable_leaf_bytes"] < referenceBytes / 2);
        var reversed = combat with { ActorOrder = order.Reverse().ToImmutableArray() };
        Assert.Equal(CanonicalJson.Serialize(reversed), Encoding.UTF8.GetString(CanonicalJson.Property("combat", reversed).Bytes.Span));
        Assert.NotEqual(CanonicalJson.ComputeHash(combat), CanonicalJson.ComputeHash(reversed));
    }

    [Fact]
    public void MutableUnmarkedArrayInputsStillUseTheReferencePath()
    {
        var values = new[] { 1, 2 };
        using var scope = CanonicalJson.BeginHashScope();
        var before = CanonicalJson.ComputeHash(values);
        values[0] = 7;
        Assert.NotEqual(before, CanonicalJson.ComputeHash(values));
        Assert.Equal(CanonicalJson.Serialize(values), Encoding.UTF8.GetString(CanonicalJson.Property("values", values).Bytes.Span));
    }
}
