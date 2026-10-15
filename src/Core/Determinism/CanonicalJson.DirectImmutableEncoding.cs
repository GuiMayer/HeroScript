using System.Buffers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Core.Determinism;

public static partial class CanonicalJson
{
    private readonly record struct ScalarEncodingKey(Type Type, object Value, long Variation);
    // Only used below an opted-in, deeply immutable aggregate: property getters
    // are stable data. Unknown/mutable public hash inputs retain the old path.
    private static readonly JsonSerializerOptions ImmutableEncodingOptions = CreateImmutableEncodingOptions();

    private static JsonSerializerOptions CreateImmutableEncodingOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Converter.GetType().IsGenericType &&
                info.Converter.GetType().GetGenericTypeDefinition() == typeof(CanonicalFallbackConverter<>))
            {
                // The fallback writes the complete original contract, including
                // discriminators. STJ must not try to add metadata a second time.
                info.PolymorphismOptions = null;
            }
            if (info.Kind != JsonTypeInfoKind.Object) return;
            var properties = info.Properties.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            info.Properties.Clear();
            for (var index = 0; index < properties.Length; index++)
            {
                properties[index].Order = index;
                info.Properties.Add(properties[index]);
            }
        });
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = resolver
        };
        options.Converters.Add(new ImmutableCanonicalConverterFactory());
        options.MakeReadOnly();
        return options;
    }

    private static CanonicalEncoding EncodeImmutableLeaf(object? value, Type declaredType)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            JsonSerializer.Serialize(writer, value, declaredType, ImmutableEncodingOptions);
        // A converter can embed a value whose standalone depth was legal. Scan
        // the finished bytes to enforce the original outer depth, without a DOM.
        var reader = new Utf8JsonReader(buffer.WrittenSpan);
        var depth = 0;
        while (reader.Read())
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                depth = System.Math.Max(depth, reader.CurrentDepth + 1);
        return new(buffer.WrittenMemory, depth);
    }

    private sealed class ImmutableCanonicalConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type)
        {
            DefaultOptions.MakeReadOnly();
            var metadata = DefaultOptions.GetTypeInfo(type);
            var scalar = Nullable.GetUnderlyingType(type) ?? type;
            if (metadata.Kind == JsonTypeInfoKind.Dictionary) return true;
            // Keep every attributed converter's real behavior, then normalize
            // its output (including enums, metadata discriminators and dates).
            if (type.GetCustomAttribute<JsonConverterAttribute>(inherit: true) != null) return true;
            return metadata.Kind switch
            {
                JsonTypeInfoKind.Object => metadata.PolymorphismOptions != null ||
                    metadata.OnSerializing != null || metadata.OnSerialized != null || metadata.NumberHandling != null ||
                    metadata.Properties.Any(property => property.IsExtensionData || property.CustomConverter != null || property.NumberHandling != null),
                JsonTypeInfoKind.Enumerable => false,
                _ => !(scalar.IsPrimitive || scalar == typeof(string) || scalar == typeof(decimal) || scalar == typeof(Guid))
            };
        }

        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(JsonElement)) return new CanonicalElementConverter();
            var scalarType = Nullable.GetUnderlyingType(type) ?? type;
            var attribute = scalarType.GetCustomAttribute<JsonConverterAttribute>(inherit: true);
            if (scalarType == typeof(DateTime) || scalarType == typeof(DateTimeOffset) ||
                (scalarType.IsEnum && (attribute == null || attribute.ConverterType == typeof(JsonStringEnumConverter))))
                return (JsonConverter)Activator.CreateInstance(typeof(CanonicalScalarConverter<>).MakeGenericType(type))!;
            // Only string-key dictionaries are emitted directly. Other keys and
            // custom dictionary converters retain their default key semantics.
            if (DefaultOptions.GetTypeInfo(type) is { Kind: JsonTypeInfoKind.Dictionary, NumberHandling: null } &&
                type.GetCustomAttribute<JsonConverterAttribute>(inherit: true) == null)
            {
                var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(candidate =>
                    candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                    candidate.GenericTypeArguments[0].IsGenericType &&
                    candidate.GenericTypeArguments[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>) &&
                    candidate.GenericTypeArguments[0].GenericTypeArguments[0] == typeof(string));
                if (enumerable != null)
                    return (JsonConverter)Activator.CreateInstance(typeof(CanonicalDictionaryConverter<,>).MakeGenericType(
                        type, enumerable.GenericTypeArguments[0].GenericTypeArguments[1]))!;
            }
            return (JsonConverter)Activator.CreateInstance(typeof(CanonicalFallbackConverter<>).MakeGenericType(type))!;
        }
    }

    private sealed class CanonicalScalarConverter<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException("The canonical writer never deserializes game data");
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            object boxed = value;
            // Date equality alone drops Kind; DateTimeOffset equality drops the
            // displayed offset. Both are relevant to the original JSON bytes.
            var variation = boxed switch { DateTime date => (long)date.Kind,
                DateTimeOffset offset => offset.Offset.Ticks, _ => 0 };
            var key = new ScalarEncodingKey(typeof(T), boxed, variation);
            var scope = ActiveHashScope.Value;
            if (scope != null && scope.Scalars.TryGetValue(key, out var known))
            { writer.WriteRawValue(known.Bytes.Span, skipInputValidation: true); return; }
            using var document = JsonSerializer.SerializeToDocument(value, typeof(T), DefaultOptions);
            var buffer = new ArrayBufferWriter<byte>();
            var depth = 0;
            using (var scalarWriter = new Utf8JsonWriter(buffer)) WriteFastElement(scalarWriter, document.RootElement, ref depth);
            var encoded = new CanonicalEncoding(buffer.WrittenMemory, depth);
            scope?.Scalars.Set(key, encoded);
            writer.WriteRawValue(encoded.Bytes.Span, skipInputValidation: true);
        }
    }

    private sealed class CanonicalDictionaryConverter<TDictionary, TValue> : JsonConverter<TDictionary>
    {
        public override TDictionary? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException("The canonical writer never deserializes game data");
        public override void Write(Utf8JsonWriter writer, TDictionary value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var pair in ((IEnumerable<KeyValuePair<string, TValue>>)value!).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                JsonSerializer.Serialize(writer, pair.Value, options);
            }
            writer.WriteEndObject();
        }
    }

    private sealed class CanonicalElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException("The canonical writer never deserializes game data");
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
        {
            if (value.ValueKind == JsonValueKind.Undefined) value.WriteTo(writer);
            var depth = 0;
            WriteFastElement(writer, value, ref depth);
            if (depth > 64) throw new JsonException("Maximum canonical serialization depth exceeded");
        }
    }

    private sealed class CanonicalFallbackConverter<T> : JsonConverter<T>
    {
        public override T? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            throw new NotSupportedException("The canonical writer never deserializes game data");
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            using var document = JsonSerializer.SerializeToDocument(value, typeof(T), DefaultOptions);
            var depth = 0;
            WriteFastElement(writer, document.RootElement, ref depth);
            if (depth > 64) throw new JsonException("Maximum canonical serialization depth exceeded");
        }
    }
}
