using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Core.Combat.Models;
using Core.Run;

namespace Core.Determinism;

public static partial class CanonicalJson
{
    private sealed record EncodedProperty(JsonPropertyInfo Metadata, JsonIgnoreCondition Ignore);
    private sealed record AggregatePlan(EncodedProperty[] Properties, bool Supported);
    private static readonly ConcurrentDictionary<Type, AggregatePlan> AggregatePlans = new();

    // Deliberately narrow: only audited aggregate boundaries are decomposed.
    // Leaves still use the real serializer, including their polymorphic converters.
    private static bool IsAggregate(Type type) => type == typeof(RunState) ||
        type == typeof(CombatState) || type == typeof(RunEncounterState);

    private static CanonicalEncoding EncodeImmutable(object? value, Type declaredType, EncodingCache cache)
    {
        var canCache = value != null && value is not string && !value.GetType().IsValueType;
        var key = new HashCacheKey(value!, declaredType, DefaultOptions);
        if (canCache && cache.TryGet(key, out var cached)) return cached;
        CanonicalEncoding encoded;
        if (value != null && IsAggregate(declaredType) && Plan(declaredType) is { Supported: true } plan)
            encoded = EncodeAggregate(value, plan, cache);
        else if (declaredType == typeof(ImmutableArray<RunEncounterState>) &&
                 value is ImmutableArray<RunEncounterState> encounters && !encounters.IsDefault)
            encoded = EncodeEncounters(encounters, cache);
        else
            encoded = EncodeStandard(value, declaredType);
        if (canCache) cache.Put(key, encoded);
        return encoded;
    }

    private static AggregatePlan Plan(Type type) => AggregatePlans.GetOrAdd(type, type =>
    {
        DefaultOptions.MakeReadOnly();
        var metadata = DefaultOptions.GetTypeInfo(type);
        if (metadata.Kind != JsonTypeInfoKind.Object || metadata.PolymorphismOptions != null ||
            metadata.OnSerializing != null || metadata.OnSerialized != null ||
            metadata.Properties.Any(p => p.IsExtensionData || p.CustomConverter != null || p.NumberHandling != null))
            return new([], false);
        var properties = metadata.Properties.Where(p => p.Get != null).Select(p => new EncodedProperty(p,
            p.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true)
                .OfType<JsonIgnoreAttribute>().SingleOrDefault()?.Condition ?? JsonIgnoreCondition.Never))
            .OrderBy(p => p.Metadata.Name, StringComparer.Ordinal).ToArray();
        // Unknown ignore policies or custom metadata keep the exact fallback.
        return new(properties, properties.All(p => p.Ignore is JsonIgnoreCondition.Never or
            JsonIgnoreCondition.Always or JsonIgnoreCondition.WhenWritingNull));
    });

    private static CanonicalEncoding EncodeAggregate(object value, AggregatePlan plan, EncodingCache cache)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_aggregate_encode");
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        var depth = 1;
        foreach (var property in plan.Properties)
        {
            if (property.Ignore == JsonIgnoreCondition.Always) continue;
            var item = property.Metadata.Get!(value);
            if (property.Ignore == JsonIgnoreCondition.WhenWritingNull && item == null) continue;
            if (property.Metadata.ShouldSerialize is { } shouldSerialize && !shouldSerialize(value, item)) continue;
            var encoded = EncodeImmutable(item, property.Metadata.PropertyType, cache);
            if (encoded.Depth >= 64) throw new JsonException("Maximum canonical serialization depth exceeded");
            writer.WritePropertyName(property.Metadata.Name);
            // Only internally generated canonical bytes reach here; depth is checked above.
            writer.WriteRawValue(encoded.Bytes.Span, skipInputValidation: true);
            depth = System.Math.Max(depth, encoded.Depth + 1);
        }
        writer.WriteEndObject();
        writer.Flush();
        return new(buffer.WrittenMemory, depth);
    }

    private static CanonicalEncoding EncodeEncounters(ImmutableArray<RunEncounterState> encounters, EncodingCache cache)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_encounters_encode");
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartArray();
        var depth = 1;
        foreach (var encounter in encounters)
        {
            var encoded = EncodeImmutable(encounter, typeof(RunEncounterState), cache);
            if (encoded.Depth >= 64) throw new JsonException("Maximum canonical serialization depth exceeded");
            writer.WriteRawValue(encoded.Bytes.Span, skipInputValidation: true);
            depth = System.Math.Max(depth, encoded.Depth + 1);
        }
        writer.WriteEndArray();
        writer.Flush();
        return new(buffer.WrittenMemory, depth);
    }

    private static CanonicalEncoding EncodeStandard(object? value, Type declaredType)
    {
        using var timing = Core.Diagnostics.OperationalTiming.Measure("canonical_standard_encode");
        var element = JsonSerializer.SerializeToElement(value, declaredType, DefaultOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        var depth = 0;
        WriteFastElement(writer, element, ref depth);
        writer.Flush();
        return new(buffer.WrittenMemory, depth);
    }

    // Nested scopes share the operation budget. No cache survives the outer scope.
    // Locks protect forked execution contexts, but encoding never holds the cache lock.
    private sealed class EncodingCache
    {
        private const int Budget = 8 * 1024 * 1024;
        private readonly object _gate = new();
        private readonly Dictionary<HashCacheKey, (CanonicalEncoding Encoding, LinkedListNode<HashCacheKey> Node)> _items =
            new(HashCacheKeyComparer.Instance);
        private readonly LinkedList<HashCacheKey> _order = new();
        private int _bytes;
        public bool TryGet(HashCacheKey key, out CanonicalEncoding encoding)
        {
            lock (_gate)
            {
                if (!_items.TryGetValue(key, out var entry)) { encoding = default; return false; }
                _order.Remove(entry.Node); _order.AddLast(entry.Node);
                encoding = entry.Encoding;
                return true;
            }
        }
        public void Put(HashCacheKey key, CanonicalEncoding encoding)
        {
            if (encoding.Bytes.Length > Budget) return;
            lock (_gate)
            {
                if (_items.ContainsKey(key)) return;
                while (_items.Count >= 128 || _bytes + encoding.Bytes.Length > Budget)
                {
                    var first = _order.First!;
                    _bytes -= _items[first.Value].Encoding.Bytes.Length;
                    _items.Remove(first.Value); _order.RemoveFirst();
                }
                var owned = encoding with { Bytes = encoding.Bytes.ToArray() };
                _items.Add(key, (owned, _order.AddLast(key)));
                _bytes += owned.Bytes.Length;
            }
        }
    }
}
