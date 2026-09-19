using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Determinism;

/// <summary>
/// Opts a deeply immutable aggregate into command-scoped canonical hash reuse.
/// Implementations must never mutate observable data after construction.
/// </summary>
public interface ICanonicalHashMemoizable { }

/// <summary>
/// Produces stable JSON and SHA-256 state hashes. Object properties are ordered
/// ordinally while array order is preserved as domain data.
/// </summary>
public static class CanonicalJson
{
    private static readonly AsyncLocal<HashScope?> ActiveHashScope = new();
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    {
        var bytes = WriteCanonicalBytes(value, options);
        return Encoding.UTF8.GetString(bytes.WrittenSpan);
    }

    public static string ComputeHash<T>(T value, JsonSerializerOptions? options = null)
    {
        var effectiveOptions = options ?? DefaultOptions;
        var scope = ActiveHashScope.Value;
        if (scope != null && value is ICanonicalHashMemoizable memoizable)
        {
            var key = new HashCacheKey(memoizable, typeof(T), effectiveOptions);
            if (scope.Hashes.TryGetValue(key, out var cached))
                return cached;
            var computed = ComputeHashCore(value, effectiveOptions);
            scope.Hashes[key] = computed;
            return computed;
        }

        return ComputeHashCore(value, effectiveOptions);
    }

    /// <summary>
    /// Reuses hashes of the same immutable aggregate instance for one command.
    /// The scope is execution-only state and never affects persisted output.
    /// </summary>
    public static IDisposable BeginHashScope()
    {
        var scope = new HashScope(ActiveHashScope.Value);
        ActiveHashScope.Value = scope;
        return new HashScopeLease(scope);
    }

    private static string ComputeHashCore<T>(T value, JsonSerializerOptions options)
    {
        var bytes = WriteCanonicalBytes(value, options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes.WrittenSpan));
    }

    private static ArrayBufferWriter<byte> WriteCanonicalBytes<T>(
        T value,
        JsonSerializerOptions? options)
    {
        var element = JsonSerializer.SerializeToElement(value, options ?? DefaultOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = false });
        WriteElement(writer, element);
        writer.Flush();
        return buffer;
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
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

    private sealed class HashScope(HashScope? previous)
    {
        public HashScope? Previous { get; } = previous;
        public Dictionary<HashCacheKey, string> Hashes { get; } = new(HashCacheKeyComparer.Instance);
    }

    private readonly record struct HashCacheKey(
        ICanonicalHashMemoizable Value,
        Type DeclaredType,
        JsonSerializerOptions Options);

    private sealed class HashCacheKeyComparer : IEqualityComparer<HashCacheKey>
    {
        public static HashCacheKeyComparer Instance { get; } = new();

        public bool Equals(HashCacheKey x, HashCacheKey y) =>
            ReferenceEquals(x.Value, y.Value) &&
            x.DeclaredType == y.DeclaredType &&
            ReferenceEquals(x.Options, y.Options);

        public int GetHashCode(HashCacheKey key) => HashCode.Combine(
            RuntimeHelpers.GetHashCode(key.Value),
            key.DeclaredType,
            RuntimeHelpers.GetHashCode(key.Options));
    }

    private sealed class HashScopeLease(HashScope scope) : IDisposable
    {
        private HashScope? _scope = scope;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _scope, null);
            if (current == null)
                return;
            if (!ReferenceEquals(ActiveHashScope.Value, current))
                throw new InvalidOperationException("Canonical hash scopes must be disposed in stack order");
            ActiveHashScope.Value = current.Previous;
        }
    }
}
