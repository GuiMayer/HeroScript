using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Core.Determinism;

public static partial class CanonicalJson
{
    // Default encoder escapes these ASCII characters. Non-ASCII and existing
    // escapes deliberately use the original normalization, not raw JSON spelling.
    private static readonly SearchValues<byte> EscapedAscii = SearchValues.Create("\"\\&'<>+`"u8);
    private readonly record struct CanonicalField(string Name, JsonElement Value, int OriginalIndex);
    private sealed class CanonicalFieldComparer : IComparer<CanonicalField>
    {
        public static readonly CanonicalFieldComparer Instance = new();
        public int Compare(CanonicalField a, CanonicalField b)
        {
            var order = StringComparer.Ordinal.Compare(a.Name, b.Name);
            // JSON may contain duplicate names; the original OrderBy is stable.
            return order != 0 ? order : a.OriginalIndex.CompareTo(b.OriginalIndex);
        }
    }

    private static void WriteFastElement(Utf8JsonWriter writer, JsonElement element, ref int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                depth = System.Math.Max(depth, writer.CurrentDepth);
                var count = element.GetPropertyCount();
                if (count == 1)
                {
                    var properties = element.EnumerateObject();
                    properties.MoveNext();
                    writer.WritePropertyName(properties.Current.Name);
                    WriteFastElement(writer, properties.Current.Value, ref depth);
                }
                else if (count > 1)
                {
                    var fields = ArrayPool<CanonicalField>.Shared.Rent(count);
                    try
                    {
                        var index = 0;
                        foreach (var property in element.EnumerateObject())
                        {
                            fields[index] = new(property.Name, property.Value, index);
                            index++;
                        }
                        Array.Sort(fields, 0, count, CanonicalFieldComparer.Instance);
                        for (var i = 0; i < count; i++)
                        {
                            writer.WritePropertyName(fields[i].Name);
                            WriteFastElement(writer, fields[i].Value, ref depth);
                        }
                    }
                    finally { ArrayPool<CanonicalField>.Shared.Return(fields, clearArray: true); }
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                depth = System.Math.Max(depth, writer.CurrentDepth);
                foreach (var item in element.EnumerateArray()) WriteFastElement(writer, item, ref depth);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var raw = JsonMarshal.GetRawUtf8Value(element);
                var content = raw[1..^1];
                if (content.IndexOfAnyExceptInRange((byte)32, (byte)126) < 0 && content.IndexOfAny(EscapedAscii) < 0)
                    writer.WriteRawValue(raw, skipInputValidation: true);
                else writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                element.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind: {element.ValueKind}.");
        }
    }
}
