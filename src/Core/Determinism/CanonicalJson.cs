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
    // Default serializer only. Weak keys cannot keep completed runs/branches alive.
    // Custom options/converters retain the existing request-scoped semantics.
    private static readonly ConditionalWeakTable<object, ImmutableHashes> ImmutableDefaults = new();
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

    public static void PrepareMetadata(params Type[] roots) =>
        SerializationMetadataWarmup.Prepare(DefaultOptions, roots);

    public static string ComputeHash<T>(T value, JsonSerializerOptions? options = null)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_hash");
        var effectiveOptions = options ?? DefaultOptions;
        if (value is ICanonicalHashMemoizable && ReferenceEquals(effectiveOptions, DefaultOptions))
        {
            var entry = ImmutableDefaults.GetValue(value, _ => new ImmutableHashes());
            lock (entry.Values)
            {
                if (entry.Values.TryGetValue(typeof(T), out var known)) return known;
                var hash = ComputeHashCore(value, effectiveOptions);
                entry.Values[typeof(T)] = hash;
                return hash;
            }
        }
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
        var bytes = CanonicalBytes(value, options);
        return Convert.ToHexStringLower(SHA256.HashData(bytes.Bytes.Span));
    }

    // Composition preserves the original embedded JSON, not a hash-of-hashes.
    // Only deeply immutable instances with the default serializer are reused.
    private readonly record struct CanonicalEncoding(ReadOnlyMemory<byte> Bytes, int Depth);
    internal readonly record struct CanonicalProperty(string Name, ReadOnlyMemory<byte> Bytes, int Depth);
    internal static CanonicalProperty Property<T>(string name, T value)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_hash");
        var encoded = CanonicalBytes(value, DefaultOptions);
        return new(name, encoded.Bytes, encoded.Depth);
    }

    internal static string ComputeObjectHash(params CanonicalProperty[] properties)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_hash");
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        string? previous = null;
        foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property.Name == previous) throw new ArgumentException("Duplicate canonical property");
            // Embedded properties add the outer object to the serializer's depth.
            // Raw writes alone would accidentally accept previously invalid data.
            if (property.Depth >= 64) throw new JsonException("Maximum canonical serialization depth exceeded");
            writer.WritePropertyName(property.Name);
            writer.WriteRawValue(property.Bytes.Span, skipInputValidation: false);
            previous = property.Name;
        }
        writer.WriteEndObject();
        writer.Flush();
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static CanonicalEncoding CanonicalBytes<T>(T value, JsonSerializerOptions options)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_encode");
        var scope = ActiveHashScope.Value;
        if (scope != null && value is ICanonicalHashMemoizable memoizable && ReferenceEquals(options, DefaultOptions))
        {
            var key = new HashCacheKey(memoizable, typeof(T), options);
            if (scope.Bytes.TryGetValue(key, out var cached)) return cached;
            var buffer = WriteCanonicalBytes(value, options, out var depth);
            // FIFO, request-local, bounded by both retained bytes and entry count.
            // Eviction changes only work performed, never serialized facts.
            const int budget = 8 * 1024 * 1024;
            if (buffer.WrittenCount > budget) return new(buffer.WrittenMemory, depth);
            while (scope.Bytes.Count >= 64 || scope.ByteCount + buffer.WrittenCount > budget)
            {
                var oldest = scope.ByteOrder.Dequeue();
                scope.ByteCount -= scope.Bytes[oldest].Bytes.Length;
                scope.Bytes.Remove(oldest);
            }
            var owned = buffer.WrittenMemory.ToArray();
            var encoding = new CanonicalEncoding(owned, depth);
            scope.Bytes.Add(key, encoding);
            scope.ByteOrder.Enqueue(key);
            scope.ByteCount += owned.Length;
            return encoding;
        }
        var uncached = WriteCanonicalBytes(value, options, out var uncachedDepth);
        return new(uncached.WrittenMemory, uncachedDepth);
    }

    private static ArrayBufferWriter<byte> WriteCanonicalBytes<T>(
        T value,
        JsonSerializerOptions? options) => WriteCanonicalBytes(value, options, out _);

    private static ArrayBufferWriter<byte> WriteCanonicalBytes<T>(
        T value, JsonSerializerOptions? options, out int depth)
    {
        var element = JsonSerializer.SerializeToElement(value, options ?? DefaultOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Indented = false });
        depth = 0;
        WriteElement(writer, element, ref depth);
        writer.Flush();
        return buffer;
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, ref int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                depth = System.Math.Max(depth, writer.CurrentDepth);
                foreach (var property in element.EnumerateObject().OrderBy(
                             property => property.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value, ref depth);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                depth = System.Math.Max(depth, writer.CurrentDepth);
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item, ref depth);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                // JsonElement writes the original numeric token without allocating a string.
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

    private sealed class HashScope(HashScope? previous)
    {
        public HashScope? Previous { get; } = previous;
        public Dictionary<HashCacheKey, string> Hashes { get; } = new(HashCacheKeyComparer.Instance);
        public Dictionary<HashCacheKey, CanonicalEncoding> Bytes { get; } = new(HashCacheKeyComparer.Instance);
        public Queue<HashCacheKey> ByteOrder { get; } = new();
        public int ByteCount { get; set; }
    }
    private sealed class ImmutableHashes { public Dictionary<Type, string> Values { get; } = new(); }

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
