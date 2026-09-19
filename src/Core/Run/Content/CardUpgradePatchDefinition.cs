using System.Text.Json.Serialization;

namespace Core.Run.Content;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardNumericPatchOperation
{
    Add,
    Multiply,
    Set
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardEffectNumericAttribute
{
    FlatValue,
    Chance,
    Repeat,
    StatusStacks,
    StatusDuration
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardTargetingNumericAttribute
{
    MinimumTargets,
    MaximumTargets
}

/// <summary>
/// A typed, permanent transformation addressed to one stable component.
/// Contextual buffs and scaling never use this contract.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CardEffectNumericPatchDefinition), "effect_numeric")]
[JsonDerivedType(typeof(CardCostAmountPatchDefinition), "cost_amount")]
[JsonDerivedType(typeof(CardInfluenceNumericPatchDefinition), "influence_numeric")]
[JsonDerivedType(typeof(CardTargetingNumericPatchDefinition), "targeting_numeric")]
[JsonDerivedType(typeof(CardDispositionPatchDefinition), "disposition")]
public abstract record CardUpgradePatchDefinition
{
    public string ComponentId { get; init; } = string.Empty;
}

public sealed record CardEffectNumericPatchDefinition : CardUpgradePatchDefinition
{
    public CardEffectNumericAttribute Attribute { get; init; }
    public CardNumericPatchOperation Operation { get; init; } = CardNumericPatchOperation.Add;
    public float Value { get; init; }
}

public sealed record CardCostAmountPatchDefinition : CardUpgradePatchDefinition
{
    public string ResourceId { get; init; } = string.Empty;
    public CardNumericPatchOperation Operation { get; init; } = CardNumericPatchOperation.Add;
    public float Value { get; init; }
}

public sealed record CardInfluenceNumericPatchDefinition : CardUpgradePatchDefinition
{
    public CardNumericPatchOperation Operation { get; init; } = CardNumericPatchOperation.Add;
    public float Value { get; init; }
}

public sealed record CardTargetingNumericPatchDefinition : CardUpgradePatchDefinition
{
    public CardTargetingNumericAttribute Attribute { get; init; }
    public CardNumericPatchOperation Operation { get; init; } = CardNumericPatchOperation.Add;
    public int Value { get; init; }
}

public sealed record CardDispositionPatchDefinition : CardUpgradePatchDefinition
{
    public string CardZoneResolutionFlowId { get; init; } = string.Empty;
}
