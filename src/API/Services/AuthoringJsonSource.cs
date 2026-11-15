using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace API.Services;

/// <summary>Rewrites changed values while preserving the rest of a source object's bytes.</summary>
internal static class AuthoringJsonSource
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    public static IEnumerable<string> ChangedProperties(byte[]? before, byte[]? after)
    {
        var previous = before is null ? new JsonObject() : ReadObject(before);
        var applied = after is null ? new JsonObject() : ReadObject(after);
        return ChangedProperties(previous, applied);
    }

    public static ReadOnlyMemory<byte>? ReverseObjectChanges(ReadOnlyMemory<byte> current, byte[] before, byte[] after)
    {
        var previous = ReadObject(before);
        var applied = ReadObject(after);
        var merged = ReadObject(current);
        foreach (var name in ChangedProperties(previous, applied))
        {
            if (!SameProperty(merged, applied, name))
                return null;
            if (previous.TryGetPropertyValue(name, out var value))
                merged[name] = value?.DeepClone();
            else
                merged.Remove(name);
        }
        return Rewrite(current, merged);
    }

    private static JsonObject ReadObject(ReadOnlyMemory<byte> bytes) =>
        JsonNode.Parse(Mods.DirectoryPackageProvider.WithoutBom(bytes).Span)!.AsObject();

    private static string[] ChangedProperties(JsonObject before, JsonObject after) =>
        before.Select(item => item.Key).Union(after.Select(item => item.Key), StringComparer.Ordinal)
            .Where(name => !SameProperty(before, after, name))
            .ToArray();

    private static bool SameProperty(JsonObject first, JsonObject second, string name) =>
        first.TryGetPropertyValue(name, out var left) == second.TryGetPropertyValue(name, out var right) &&
        JsonNode.DeepEquals(left, right);

    public static ReadOnlyMemory<byte> Rewrite(ReadOnlyMemory<byte>? source, JsonObject candidate)
    {
        if (source is null)
            return Encoding.UTF8.GetBytes(candidate.ToJsonString(Indented) + "\n");

        var original = source.Value;
        var bytes = Mods.DirectoryPackageProvider.WithoutBom(original);
        var text = Encoding.UTF8.GetString(bytes.Span);
        return Encoding.UTF8.GetBytes((original.Length != bytes.Length ? "\uFEFF" : "") + RewriteObject(text, candidate));
    }

    private static string RewriteObject(string text, JsonObject candidate)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var multiline = text.Contains('\n');
        var existing = Properties(text);
        using var parsed = JsonDocument.Parse(text);
        var wanted = JsonSerializer.SerializeToElement(candidate);
        foreach (var property in existing)
        {
            if (wanted.TryGetProperty(property.Name, out var replacement) &&
                JsonElement.DeepEquals(parsed.RootElement.GetProperty(property.Name), replacement))
                continue;
            text = Edit(text, property.Name, candidate[property.Name], wanted.TryGetProperty(property.Name, out _),
                multiline, newline);
        }

        foreach (var property in wanted.EnumerateObject())
            if (!parsed.RootElement.TryGetProperty(property.Name, out _))
                text = Edit(text, property.Name, candidate[property.Name], true, multiline, newline);

        return text;
    }

    private static string Edit(string text, string name, JsonNode? value, bool keep, bool multiline, string newline)
    {
        var properties = Properties(text);
        var index = properties.FindIndex(item => item.Name == name);
        if (index >= 0)
        {
            var property = properties[index];
            if (keep)
            {
                var current = text[property.ValueStart..property.End];
                var updated = RewriteValue(current, value, Indent(text, property.Start), newline);
                return text[..property.ValueStart] + updated + text[property.End..];
            }
            if (index + 1 < properties.Count)
                return text[..property.Start] + text[properties[index + 1].Start..];
            var from = index == 0 ? property.Start : properties[index - 1].End;
            return text[..from] + text[property.End..];
        }

        var last = properties.LastOrDefault();
        var position = last?.End ?? text.IndexOf('{') + 1;
        var indent = multiline ? last is null ? "  " : Indent(text, last.Start) : "";
        var prefix = (last is null ? "" : ",") + (multiline ? newline + indent : "");
        var entry = JsonSerializer.Serialize(name) + (multiline ? ": " : ":") +
            Format(value, multiline, indent, newline);
        var suffix = last is null && multiline && !text.AsSpan(position).Contains('\n') ? newline : "";
        return text[..position] + prefix + entry + suffix + text[position..];
    }

    private static string RewriteValue(string current, JsonNode? value, string indent, string newline)
    {
        if (current[0] == '{' && value is JsonObject item)
            return RewriteObject(current, item);
        if (current[0] == '[' && value is JsonArray array)
        {
            var spans = Elements(current);
            if (spans.Count == array.Count)
            {
                using var parsed = JsonDocument.Parse(current);
                for (var index = spans.Count - 1; index >= 0; index--)
                {
                    if (JsonElement.DeepEquals(parsed.RootElement[index], JsonSerializer.SerializeToElement(array[index])))
                        continue;
                    var (start, end) = spans[index];
                    var updated = RewriteValue(current[start..end], array[index], Indent(current, start), newline);
                    current = current[..start] + updated + current[end..];
                }
                return current;
            }
        }
        return Format(value, current.Contains('\n'), indent, newline);
    }

    private static string Format(JsonNode? node, bool multiline, string indent, string newline) =>
        node?.ToJsonString(multiline ? Indented : null).Replace("\n", newline + indent, StringComparison.Ordinal) ?? "null";

    private static string Indent(string text, int position)
    {
        var line = text.LastIndexOf('\n', position) + 1;
        var end = line;
        while (end < position && text[end] is ' ' or '\t')
            end++;
        return text[line..end];
    }

    private static List<PropertySpan> Properties(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        reader.Read();
        var result = new List<PropertySpan>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var start = (int)reader.TokenStartIndex;
            var name = reader.GetString()!;
            reader.Read();
            var value = (int)reader.TokenStartIndex;
            reader.Skip();
            result.Add(new(name, Encoding.UTF8.GetCharCount(bytes.AsSpan(0, start)),
                Encoding.UTF8.GetCharCount(bytes.AsSpan(0, value)),
                Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.BytesConsumed))));
        }
        return result;
    }

    private static List<(int Start, int End)> Elements(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes);
        reader.Read();
        var result = new List<(int, int)>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var start = (int)reader.TokenStartIndex;
            reader.Skip();
            result.Add((Encoding.UTF8.GetCharCount(bytes.AsSpan(0, start)),
                Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.BytesConsumed))));
        }
        return result;
    }

    private sealed record PropertySpan(string Name, int Start, int ValueStart, int End);
}
