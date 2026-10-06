using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Effects;

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
[JsonDerivedType(typeof(CardBundlePatchDefinition), "bundle")]
[JsonDerivedType(typeof(CardBundleSnapshotPatchDefinition), "bundle_snapshot")]
[JsonDerivedType(typeof(CardEffectNumericPatchDefinition), "effect_numeric")]
[JsonDerivedType(typeof(CardCostAmountPatchDefinition), "cost_amount")]
[JsonDerivedType(typeof(CardInfluenceNumericPatchDefinition), "influence_numeric")]
[JsonDerivedType(typeof(CardTargetingNumericPatchDefinition), "targeting_numeric")]
[JsonDerivedType(typeof(CardDispositionPatchDefinition), "disposition")]
[JsonDerivedType(typeof(CardTagsPatchDefinition), "tags")]
[JsonDerivedType(typeof(CardComponentPatchDefinition), "component")]
[JsonDerivedType(typeof(CardEffectParameterNumericPatchDefinition), "effect_parameter_numeric")]
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardComponentPatchOperation { Add, Remove, Replace }

public sealed record CardComponentPatchDefinition : CardUpgradePatchDefinition
{
    public CardComponentPatchOperation Operation { get; init; }
    /// <summary>Add/Replace preserve the addressed ComponentId. Remove must not carry a component.</summary>
    public CardComponentDefinition? Component { get; init; }
}

public sealed record CardTagsPatchDefinition : CardUpgradePatchDefinition
{
    private ImmutableArray<string> _add = [];
    private ImmutableArray<string> _remove = [];
    public IReadOnlyList<string> Add
    {
        get => _add;
        init => _add = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> Remove
    {
        get => _remove;
        init => _remove = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>Changes only the permanent flat base of an existing typed parameter, never its pipeline result.</summary>
public sealed record CardEffectParameterNumericPatchDefinition : CardUpgradePatchDefinition
{
    public EffectNumericParameter Parameter { get; init; }
    public CardNumericPatchOperation Operation { get; init; } = CardNumericPatchOperation.Add;
    public float Value { get; init; }
}
