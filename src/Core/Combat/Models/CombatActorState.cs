using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Resources;

namespace Core.Combat.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ResourceEntityComponentState), "resources")]
[JsonDerivedType(typeof(StatEntityComponentState), "stats")]
[JsonDerivedType(typeof(InventoryEntityComponentState), "inventory")]
[JsonDerivedType(typeof(AbilityEntityComponentState), "abilities")]
public abstract record EntityComponentState
{
    public string ComponentId { get; init; } = string.Empty;
}

public sealed record ResourceEntityComponentState : EntityComponentState
{
    public ResourceSet State { get; init; } = new();
}

public sealed record StatEntityComponentState : EntityComponentState
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

public sealed record InventoryEntityComponentState : EntityComponentState
{
    private ImmutableArray<string> _itemIds = [];
    public int Capacity { get; init; } = -1;
    public IReadOnlyList<string> ItemIds
    {
        get => _itemIds;
        init => _itemIds = value?.ToImmutableArray() ?? [];
    }
}

public sealed record AbilityEntityComponentState : EntityComponentState
{
    private ImmutableArray<string> _abilityIds = [];
    public IReadOnlyList<string> AbilityIds
    {
        get => _abilityIds;
        init => _abilityIds = value?.ToImmutableArray() ?? [];
    }
}

public record EntityState
{
    protected ImmutableDictionary<string, EntityComponentState> _components =
        ImmutableDictionary<string, EntityComponentState>.Empty.WithComparers(StringComparer.Ordinal);

    public string InstanceId { get; init; } = string.Empty;
    public string DefinitionId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, EntityComponentState> Components
    {
        get => _components;
        init => _components = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, EntityComponentState>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public T? Component<T>(string? componentId = null) where T : EntityComponentState =>
        _components.Values.OfType<T>().FirstOrDefault(component =>
            componentId == null || string.Equals(component.ComponentId, componentId, StringComparison.Ordinal));
}

/// <summary>Immutable runtime actor; side relationships and binding define its role.</summary>
public sealed record CombatActorState : EntityState
{
    public string SideId { get; init; } = string.Empty;
    public ControllerBinding ControllerBinding { get; init; } = new();

    [JsonIgnore]
    public ResourceSet ResourceState
    {
        get => Component<ResourceEntityComponentState>()?.State
            ?? new ResourceSet { OwnerId = InstanceId };
        init
        {
            var component = Component<ResourceEntityComponentState>()
                ?? new ResourceEntityComponentState { ComponentId = "resources" };
            _components = _components.SetItem(component.ComponentId, component with { State = value });
        }
    }

    [JsonIgnore]
    public bool IsAlive => !ResourceThresholdEvaluator.IsOwnerDefeated(ResourceState.Resources.Values);

    public ResourcePool? GetResource(string resourceId) => ResourceState.Get(resourceId);

    public CombatActorState WithResourceState(ResourceSet state)
    {
        var component = Component<ResourceEntityComponentState>()
            ?? new ResourceEntityComponentState { ComponentId = "resources" };
        return this with
        {
            Components = Components.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(component.ComponentId, component with { State = state })
        };
    }

    public Result<CombatActorState> ApplyResourceMutation(
        string mutationId,
        string resourceId,
        ResourceMutationOperation operation,
        float value,
        ResourceValueField field = ResourceValueField.Current)
    {
        var applied = ResourceState.Apply([
            new ResolvedResourceMutation
            {
                MutationId = mutationId,
                ResourceId = resourceId,
                Field = field,
                Operation = operation,
                Value = value
            }
        ]);
        return applied.IsFailure
            ? Result<CombatActorState>.Failure(applied.Error)
            : Result<CombatActorState>.Success(WithResourceState(applied.Value.State));
    }
}
