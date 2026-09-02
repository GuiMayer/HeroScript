using System.Text.Json.Serialization;

namespace Core.Effects;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourceEffectOperation
{
    ADD,
    SUBTRACT,
    SET
}
