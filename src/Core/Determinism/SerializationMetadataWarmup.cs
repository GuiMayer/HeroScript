using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Core.Determinism;

/// <summary>Prepares serializer type metadata, not game data, states or hash results.</summary>
public static class SerializationMetadataWarmup
{
    public static void Prepare(JsonSerializerOptions options, params Type[] roots)
    {
        options.MakeReadOnly(populateMissingResolver: true);
        var pending = new Stack<Type>(roots);
        var visited = new HashSet<Type>();
        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type)) continue;
            var metadata = options.GetTypeInfo(type);
            if (metadata.Kind == JsonTypeInfoKind.Object)
                foreach (var property in metadata.Properties)
                    pending.Push(property.PropertyType);
            if (metadata.PolymorphismOptions != null)
                foreach (var derived in metadata.PolymorphismOptions.DerivedTypes)
                    pending.Push(derived.DerivedType);
            if (type.IsArray) pending.Push(type.GetElementType()!);
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments()) pending.Push(argument);
        }
    }
}
