using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace API.Services;

/// <summary>Rewrites changed values while preserving the rest of a source object's bytes.</summary>
internal static class AuthoringJsonSource
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static ReadOnlyMemory<byte> Rewrite(ReadOnlyMemory<byte>? source, JsonObject candidate)
    {
        if (source is null)
            return Encoding.UTF8.GetBytes(candidate.ToJsonString(Indented) + "\n");

        var original = source.Value;
        var bytes = Mods.DirectoryPackageProvider.WithoutBom(original);
        var text = Encoding.UTF8.GetString(bytes.Span);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var multiline = text.Contains('\n');
        var existing = Properties(text);
        using var parsed = JsonDocument.Parse(bytes);
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

        return Encoding.UTF8.GetBytes((original.Length != bytes.Length ? "\uFEFF" : "") + text);
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
                var expanded = text.AsSpan(property.ValueStart, property.End - property.ValueStart).Contains('\n');
                return text[..property.ValueStart] + Format(value, expanded, Indent(text, property.Start), newline) +
                    text[property.End..];
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

    private sealed record PropertySpan(string Name, int Start, int ValueStart, int End);
}
