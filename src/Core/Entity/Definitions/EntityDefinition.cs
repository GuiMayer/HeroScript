using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Core.Entity.Definitions;

/// <summary>
/// An entity definition is a role-free container of immutable components.
/// Combat ownership and control belong to the participant that materializes it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EntityDefinition
{
    private ImmutableArray<EntityComponentDefinition> _components = [];
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    public string DefinitionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string SpritePath { get; init; } = string.Empty;
    public IReadOnlyList<EntityComponentDefinition> Components
    {
        get => _components;
        init => _components = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public T? Component<T>(string? componentId = null) where T : EntityComponentDefinition =>
        _components.OfType<T>().FirstOrDefault(component =>
            componentId == null || string.Equals(component.ComponentId, componentId, StringComparison.Ordinal));
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResourceEntityComponentDefinition), "resources")]
[JsonDerivedType(typeof(StatEntityComponentDefinition), "stats")]
[JsonDerivedType(typeof(InventoryEntityComponentDefinition), "inventory")]
[JsonDerivedType(typeof(AbilityEntityComponentDefinition), "abilities")]
public abstract record EntityComponentDefinition
{
    public string ComponentId { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourceEntityComponentDefinition : EntityComponentDefinition
{
    private ImmutableDictionary<string, ResourcePoolDefinition> _pools =
        ImmutableDictionary<string, ResourcePoolDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ResourcePoolDefinition> Pools
    {
        get => _pools;
        init => _pools = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, ResourcePoolDefinition>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResourcePoolDefinition
{
    public float Current { get; init; }
    public float Max { get; init; }
}

/// <summary>Stats have no built-in semantics; formulas opt into values by ID.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StatEntityComponentDefinition : EntityComponentDefinition
{
    private ImmutableDictionary<string, float> _values =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, float> Values
    {
        get => _values;
        init => _values = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InventoryEntityComponentDefinition : EntityComponentDefinition
{
    private ImmutableArray<string> _startingItems = [];
    public int Capacity { get; init; } = -1;
    public IReadOnlyList<string> StartingItems
    {
        get => _startingItems;
        init => _startingItems = value?.ToImmutableArray() ?? [];
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AbilityEntityComponentDefinition : EntityComponentDefinition
{
    private ImmutableArray<string> _abilityIds = [];
    public IReadOnlyList<string> AbilityIds
    {
        get => _abilityIds;
        init => _abilityIds = value?.ToImmutableArray() ?? [];
    }
}
