using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;
using Core.Effects;

namespace Core.Run.Content;

/// <summary>
/// A stable, typed unit of card behavior. The discriminator is part of authored
/// JSON while ComponentId is the permanent address used by upgrades and traces.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CardCostComponentDefinition), "cost")]
[JsonDerivedType(typeof(CardEffectComponentDefinition), "effect")]
[JsonDerivedType(typeof(CardConditionComponentDefinition), "condition")]
[JsonDerivedType(typeof(CardTargetingComponentDefinition), "targeting")]
[JsonDerivedType(typeof(CardDispositionComponentDefinition), "disposition")]
[JsonDerivedType(typeof(CardTriggerComponentDefinition), "trigger")]
[JsonDerivedType(typeof(CardInfluenceComponentDefinition), "influence")]
public abstract record CardComponentDefinition
{
    public string ComponentId { get; init; } = string.Empty;
    public int Order { get; init; }
}

public sealed record CardCostComponentDefinition : CardComponentDefinition
{
    public ActionCosts Costs { get; init; } = new();
}

public sealed record CardEffectComponentDefinition : CardComponentDefinition
{
    public EffectDefinition Effect { get; init; } = new();
}

public sealed record CardConditionComponentDefinition : CardComponentDefinition
{
    public string Expression { get; init; } = string.Empty;
    public string FailureReason { get; init; } = "Condition not met";
}

public sealed record CardTargetingComponentDefinition : CardComponentDefinition
{
    public EffectTarget Target { get; init; } = EffectTarget.TARGET;
    public int MinimumTargets { get; init; } = 1;
    public int MaximumTargets { get; init; } = 1;
    public bool AllowSelf { get; init; }
}

public sealed record CardDispositionComponentDefinition : CardComponentDefinition
{
    public CardConsumeDestination Destination { get; init; } = CardConsumeDestination.Discard;
}

public sealed record CardTriggerComponentDefinition : CardComponentDefinition
{
    private ImmutableArray<EffectDefinition> _effects = [];

    public string Boundary { get; init; } = string.Empty;
    public IReadOnlyList<EffectDefinition> Effects
    {
        get => _effects;
        init => _effects = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CardInfluenceComponentDefinition : CardComponentDefinition
{
    public string Channel { get; init; } = string.Empty;
    public string Bucket { get; init; } = string.Empty;
    public float? Value { get; init; }
    public string? Formula { get; init; }
    public int Priority { get; init; }
}

public sealed record CardComponentBundleDefinition
{
    private ImmutableArray<CardComponentDefinition> _components = [];

    public string BundleId { get; init; } = string.Empty;
    public IReadOnlyList<CardComponentDefinition> Components
    {
        get => _components;
        init => _components = value?.ToImmutableArray() ?? [];
    }
}
