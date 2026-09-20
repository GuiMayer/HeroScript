using System.Globalization;
using System.Text.Json.Nodes;

namespace Core.Config.Delta;

internal readonly record struct DeltaTargetPathSegment(string? PropertyName, int? ArrayIndex)
{
    public bool IsProperty => PropertyName != null;

    public static DeltaTargetPathSegment Property(string name) => new(name, null);

    public static DeltaTargetPathSegment Index(int index) => new(null, index);
}

/// <summary>
/// Parses and applies the deliberately small target-path grammar used by deltas.
/// Supported examples: <c>field</c>, <c>nested.field</c>,
/// <c>items[1]</c> and <c>items[1].value</c>.
/// </summary>
internal static class DeltaTargetPath
{
    public static bool TryParse(
        string? path,
        out IReadOnlyList<DeltaTargetPathSegment> segments,
        out string error)
    {
        var parsed = new List<DeltaTargetPathSegment>();
        segments = parsed;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Target path cannot be empty";
            return false;
        }

        var value = path.Trim();
        var index = 0;
        if (value[index] == '$')
        {
            index++;
            if (index < value.Length && value[index] == '.')
                index++;
            else if (index < value.Length && value[index] != '[')
            {
                error = $"Target path '{path}' must use '$.' before a root property";
                return false;
            }
        }

        var requiresSegment = true;
        while (index < value.Length)
        {
            if (value[index] == '.')
            {
                error = $"Target path '{path}' contains an empty property segment";
                return false;
            }

            if (value[index] == '[')
            {
                var end = value.IndexOf(']', index + 1);
                if (end < 0)
                {
                    error = $"Target path '{path}' contains an unterminated array index";
                    return false;
                }

                var token = value[(index + 1)..end];
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var arrayIndex) || arrayIndex < 0)
                {
                    error = $"Target path '{path}' contains an invalid array index '{token}'";
                    return false;
                }

                parsed.Add(DeltaTargetPathSegment.Index(arrayIndex));
                index = end + 1;
                requiresSegment = false;
            }
            else
            {
                var start = index;
                while (index < value.Length && value[index] is not '.' and not '[' and not ']')
                    index++;

                if (index < value.Length && value[index] == ']')
                {
                    error = $"Target path '{path}' contains an unexpected closing bracket";
                    return false;
                }

                var property = value[start..index];
                if (string.IsNullOrWhiteSpace(property))
                {
                    error = $"Target path '{path}' contains an empty property segment";
                    return false;
                }

                parsed.Add(DeltaTargetPathSegment.Property(property));
                requiresSegment = false;
            }

            if (index >= value.Length)
                break;

            if (value[index] == '.')
            {
                index++;
                requiresSegment = true;
                if (index >= value.Length || value[index] is '.' or '[')
                {
                    error = $"Target path '{path}' contains an empty property segment";
                    return false;
                }
            }
            else if (value[index] != '[')
            {
                error = $"Target path '{path}' contains unsupported syntax at position {index}";
                return false;
            }
        }

        if (requiresSegment || parsed.Count == 0)
        {
            error = $"Target path '{path}' does not identify a field or array item";
            return false;
        }

        if (!parsed[0].IsProperty)
        {
            error = $"Target path '{path}' must start with a root property";
            return false;
        }

        return true;
    }

    public static bool TryDelete(
        JsonNode root,
        IReadOnlyList<DeltaTargetPathSegment> segments,
        out string error)
    {
        JsonNode? current = root;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Count - 1;
            if (segment.IsProperty)
            {
                if (current is not JsonObject objectNode)
                {
                    error = $"Path '{FormatPrefix(segments, index)}' requires an object";
                    return false;
                }

                var propertyName = segment.PropertyName!;
                if (isLast)
                {
                    if (!objectNode.Remove(propertyName))
                    {
                        error = $"Field '{Format(segments)}' was not found";
                        return false;
                    }

                    error = string.Empty;
                    return true;
                }

                if (!objectNode.TryGetPropertyValue(propertyName, out current) || current == null)
                {
                    error = $"Path '{FormatPrefix(segments, index)}' was not found";
                    return false;
                }
            }
            else
            {
                if (current is not JsonArray arrayNode)
                {
                    error = $"Path '{FormatPrefix(segments, index)}' requires an array";
                    return false;
                }

                var arrayIndex = segment.ArrayIndex!.Value;
                if (arrayIndex >= arrayNode.Count)
                {
                    error = $"Array index {arrayIndex} is outside [0, {arrayNode.Count - 1}] at '{FormatPrefix(segments, index)}'";
                    return false;
                }

                if (isLast)
                {
                    arrayNode.RemoveAt(arrayIndex);
                    error = string.Empty;
                    return true;
                }

                current = arrayNode[arrayIndex];
                if (current == null)
                {
                    error = $"Path '{FormatPrefix(segments, index)}' resolves to null";
                    return false;
                }
            }
        }

        error = "Target path did not identify a removable value";
        return false;
    }

    private static string Format(IReadOnlyList<DeltaTargetPathSegment> segments) =>
        FormatPrefix(segments, segments.Count - 1);

    private static string FormatPrefix(IReadOnlyList<DeltaTargetPathSegment> segments, int lastIndex)
    {
        var result = string.Empty;
        for (var index = 0; index <= lastIndex; index++)
        {
            var segment = segments[index];
            result += segment.IsProperty
                ? (result.Length == 0 ? segment.PropertyName : $".{segment.PropertyName}")
                : $"[{segment.ArrayIndex}]";
        }

        return result;
    }
}
