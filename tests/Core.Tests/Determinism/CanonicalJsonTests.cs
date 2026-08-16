using Core.Determinism;
using Xunit;

namespace Core.Tests.Determinism;

[Trait("Category", "Unit")]
public class CanonicalJsonTests
{
    [Fact]
    public void Serialize_OrdersObjectAndDictionaryPropertiesOrdinally()
    {
        var value = new
        {
            Zebra = 1,
            Attributes = new Dictionary<string, int>
            {
                ["speed"] = 3,
                ["armor"] = 8
            }
        };

        var json = CanonicalJson.Serialize(value);

        Assert.Equal("{\"attributes\":{\"armor\":8,\"speed\":3},\"zebra\":1}", json);
    }

    [Fact]
    public void ComputeHash_IsIndependentOfDictionaryInsertionOrder()
    {
        var first = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };
        var second = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };

        Assert.Equal(CanonicalJson.ComputeHash(first), CanonicalJson.ComputeHash(second));
    }

    [Fact]
    public void ComputeHash_PreservesArrayOrderAsDomainMeaning()
    {
        Assert.NotEqual(
            CanonicalJson.ComputeHash(new[] { "a", "b" }),
            CanonicalJson.ComputeHash(new[] { "b", "a" }));
    }
}
