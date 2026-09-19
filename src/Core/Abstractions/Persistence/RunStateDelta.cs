using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Run;

namespace Core.Abstractions.Persistence;

public sealed record RunStatePatchOperation
{
    public string Operation { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public JsonElement? Value { get; init; }
}

public sealed record RunStateDelta
{
    private ImmutableArray<RunStatePatchOperation> _operations = [];

    public IReadOnlyList<RunStatePatchOperation> Operations
    {
        get => _operations;
        init => _operations = value?.ToImmutableArray() ?? [];
    }

    public static RunStateDelta Create(RunState previous, RunState current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var before = JsonSerializer.SerializeToNode(previous, RunCommitJson.Options)
            ?? throw new InvalidOperationException("Failed to serialize previous run state");
        var after = JsonSerializer.SerializeToNode(current, RunCommitJson.Options)
            ?? throw new InvalidOperationException("Failed to serialize current run state");
        var operations = new List<RunStatePatchOperation>();
        Diff(before, after, string.Empty, operations);
        if (operations.Count == 0)
            throw new InvalidOperationException("A run-state delta cannot be empty");
        return new RunStateDelta { Operations = operations };
    }

    public static RunState Apply(RunState previous, IReadOnlyList<RunStatePatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(operations);
        var root = JsonSerializer.SerializeToNode(previous, RunCommitJson.Options)
            ?? throw new InvalidOperationException("Failed to serialize run state for delta application");
        foreach (var operation in operations)
            root = Apply(root, operation);
        return root.Deserialize<RunState>(RunCommitJson.Options)
            ?? throw new InvalidDataException("Run-state delta produced an empty state");
    }

    private static void Diff(
        JsonNode? before,
        JsonNode? after,
        string path,
        ICollection<RunStatePatchOperation> operations)
    {
        if (JsonNode.DeepEquals(before, after))
            return;
        if (before is JsonObject beforeObject && after is JsonObject afterObject)
        {
            foreach (var name in beforeObject.Select(item => item.Key)
                         .Except(afterObject.Select(item => item.Key), StringComparer.Ordinal)
                         .OrderByDescending(name => name, StringComparer.Ordinal))
            {
                operations.Add(new RunStatePatchOperation
                {
                    Operation = "remove",
                    Path = ChildPath(path, name)
                });
            }
            foreach (var name in afterObject.Select(item => item.Key)
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!beforeObject.TryGetPropertyValue(name, out var previousValue))
                {
                    operations.Add(new RunStatePatchOperation
                    {
                        Operation = "add",
                        Path = ChildPath(path, name),
                        Value = ToElement(afterObject[name])
                    });
                }
                else
                {
                    Diff(previousValue, afterObject[name], ChildPath(path, name), operations);
                }
            }
            return;
        }
        if (before is JsonArray beforeArray && after is JsonArray afterArray)
        {
            var shared = System.Math.Min(beforeArray.Count, afterArray.Count);
            for (var index = 0; index < shared; index++)
                Diff(beforeArray[index], afterArray[index], ChildPath(path, index.ToString()), operations);
            for (var index = beforeArray.Count - 1; index >= afterArray.Count; index--)
            {
                operations.Add(new RunStatePatchOperation
                {
                    Operation = "remove",
                    Path = ChildPath(path, index.ToString())
                });
            }
            for (var index = shared; index < afterArray.Count; index++)
            {
                operations.Add(new RunStatePatchOperation
                {
                    Operation = "add",
                    Path = ChildPath(path, index.ToString()),
                    Value = ToElement(afterArray[index])
                });
            }
            return;
        }

        operations.Add(new RunStatePatchOperation
        {
            Operation = "replace",
            Path = path,
            Value = ToElement(after)
        });
    }

    private static JsonNode Apply(JsonNode root, RunStatePatchOperation operation)
    {
        if (string.IsNullOrEmpty(operation.Path))
        {
            if (!string.Equals(operation.Operation, "replace", StringComparison.Ordinal) ||
                operation.Value is not { } replacement)
                throw new InvalidDataException("Only replace is valid for the run-state root");
            return JsonNode.Parse(replacement.GetRawText())
                ?? throw new InvalidDataException("Run-state root replacement is null");
        }

        var segments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Unescape)
            .ToArray();
        JsonNode parent = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            parent = parent switch
            {
                JsonObject item when item[segments[index]] is { } child => child,
                JsonArray item when ParseIndex(segments[index], item.Count, allowEnd: false) is var childIndex =>
                    item[childIndex] ?? throw new InvalidDataException($"Null delta path: {operation.Path}"),
                _ => throw new InvalidDataException($"Invalid delta path: {operation.Path}")
            };
        }

        var leaf = segments[^1];
        var value = operation.Value is { } element
            ? JsonNode.Parse(element.GetRawText())
            : null;
        if (parent is JsonObject parentObject)
        {
            if (string.Equals(operation.Operation, "remove", StringComparison.Ordinal))
            {
                if (!parentObject.Remove(leaf))
                    throw new InvalidDataException($"Delta remove path not found: {operation.Path}");
            }
            else if (string.Equals(operation.Operation, "add", StringComparison.Ordinal) ||
                     string.Equals(operation.Operation, "replace", StringComparison.Ordinal))
            {
                parentObject[leaf] = value;
            }
            else
            {
                throw new InvalidDataException($"Unsupported delta operation: {operation.Operation}");
            }
            return root;
        }
        if (parent is JsonArray parentArray)
        {
            var allowEnd = string.Equals(operation.Operation, "add", StringComparison.Ordinal);
            var arrayIndex = ParseIndex(leaf, parentArray.Count, allowEnd);
            if (string.Equals(operation.Operation, "remove", StringComparison.Ordinal))
                parentArray.RemoveAt(arrayIndex);
            else if (string.Equals(operation.Operation, "add", StringComparison.Ordinal))
                parentArray.Insert(arrayIndex, value);
            else if (string.Equals(operation.Operation, "replace", StringComparison.Ordinal))
                parentArray[arrayIndex] = value;
            else
                throw new InvalidDataException($"Unsupported delta operation: {operation.Operation}");
            return root;
        }
        throw new InvalidDataException($"Invalid delta parent: {operation.Path}");
    }

    private static int ParseIndex(string segment, int count, bool allowEnd)
    {
        if (!int.TryParse(segment, out var index) || index < 0 || index > count || (!allowEnd && index == count))
            throw new InvalidDataException($"Invalid delta array index: {segment}");
        return index;
    }

    private static JsonElement ToElement(JsonNode? node) =>
        node == null
            ? JsonSerializer.SerializeToElement<object?>(null, RunCommitJson.Options)
            : JsonSerializer.SerializeToElement(node, RunCommitJson.Options);

    private static string ChildPath(string path, string segment) =>
        $"{path}/{Escape(segment)}";

    private static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");
    private static string Unescape(string segment) => segment.Replace("~1", "/").Replace("~0", "~");
}
