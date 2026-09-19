using System.Text.Json;
using System.Text.Json.Serialization;
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

    [Fact]
    public void HashScope_ReusesOnlyTheSameOptedInImmutableInstance()
    {
        var converter = new CountingConverter();
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        var value = new MemoizableValue(7);

        string first;
        using (CanonicalJson.BeginHashScope())
        {
            first = CanonicalJson.ComputeHash(value, options);
            Assert.Equal(first, CanonicalJson.ComputeHash(value, options));
            Assert.Equal(1, converter.Writes);
            Assert.NotEqual(first, CanonicalJson.ComputeHash(new MemoizableValue(8), options));
            Assert.Equal(2, converter.Writes);
        }

        Assert.Equal(first, CanonicalJson.ComputeHash(value, options));
        Assert.Equal(3, converter.Writes);
    }

    private sealed record MemoizableValue(int Value) : ICanonicalHashMemoizable;

    private sealed class CountingConverter : JsonConverter<MemoizableValue>
    {
        public int Writes { get; private set; }

        public override MemoizableValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            new(reader.GetInt32());

        public override void Write(
            Utf8JsonWriter writer,
            MemoizableValue value,
            JsonSerializerOptions options)
        {
            Writes++;
            writer.WriteNumberValue(value.Value);
        }
    }
}
