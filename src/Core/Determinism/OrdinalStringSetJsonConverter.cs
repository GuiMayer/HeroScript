using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Determinism;

/// <summary>Preserves ordinal membership before a culture-sensitive default set converter can merge strings.</summary>
public sealed class OrdinalStringSetJsonConverter : JsonConverter<ImmutableSortedSet<string>>
{
    public override ImmutableSortedSet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var items = JsonSerializer.Deserialize<string[]>(ref reader, options)
            ?? throw new JsonException("An ordinal string set must be an array");
        if (items.Any(item => item == null)) throw new JsonException("An ordinal string set cannot contain null");
        return items.ToImmutableSortedSet(StringComparer.Ordinal);
    }

    public override void Write(Utf8JsonWriter writer, ImmutableSortedSet<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value.OrderBy(item => item, StringComparer.Ordinal)) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
