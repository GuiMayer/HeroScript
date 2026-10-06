using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Entity.Definitions;
using Core.Resources;

namespace Core.Entity.Integration;

/// <summary>One-way deterministic materialization from pinned content to immutable state.</summary>
public sealed class EntityMaterializer
{
    private readonly IResourceManager _resources;

    public EntityMaterializer(IResourceManager resources) =>
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));

    public Result<CombatActorState> Materialize(
        EntityDefinition definition,
        string instanceId,
        string contentRevision,
        string sideId,
        ControllerBinding controllerBinding,
        string? configName = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(controllerBinding);
        if (string.IsNullOrWhiteSpace(instanceId))
            return Result<CombatActorState>.Failure("Entity instanceId is required");
        if (string.IsNullOrWhiteSpace(contentRevision))
            return Result<CombatActorState>.Failure("Entity contentRevision is required");
        if (string.IsNullOrWhiteSpace(sideId))
            return Result<CombatActorState>.Failure("Combat participant sideId is required");
        if (!Enum.IsDefined(controllerBinding.Kind))
            return Result<CombatActorState>.Failure("Combat participant controller binding is invalid");
        var controllerValidation = ControllerBindingValidator.Validate(controllerBinding);
        if (controllerValidation.IsFailure)
            return Result<CombatActorState>.Failure(controllerValidation.Error);

        var states = ImmutableDictionary.CreateBuilder<string, EntityComponentState>(StringComparer.Ordinal);
        foreach (var component in definition.Components.OrderBy(item => item.ComponentId, StringComparer.Ordinal))
        {
            var state = MaterializeComponent(component, instanceId, contentRevision, configName);
            if (state.IsFailure)
                return Result<CombatActorState>.Failure(state.Error);
            if (!states.TryAdd(component.ComponentId, state.Value))
                return Result<CombatActorState>.Failure($"Duplicate entity componentId: {component.ComponentId}");
        }

        return Result<CombatActorState>.Success(new CombatActorState
        {
            InstanceId = instanceId,
            DefinitionId = definition.DefinitionId,
            ContentRevision = contentRevision,
            Name = definition.DisplayName,
            SideId = sideId,
            ControllerBinding = controllerBinding,
            Components = states.ToImmutable()
        });
    }

    private Result<EntityComponentState> MaterializeComponent(
        EntityComponentDefinition component,
        string ownerId,
        string contentRevision,
        string? configName)
    {
        return component switch
        {
            ResourceEntityComponentDefinition resources => MaterializeResources(resources, ownerId, contentRevision, configName),
            StatEntityComponentDefinition stats => Result<EntityComponentState>.Success(new StatEntityComponentState
            {
                ComponentId = stats.ComponentId,
                Values = stats.Values,
                ValueRules = stats.ValueRules
            }),
            InventoryEntityComponentDefinition inventory => Result<EntityComponentState>.Success(new InventoryEntityComponentState
            {
                ComponentId = inventory.ComponentId,
                Capacity = inventory.Capacity,
                ItemIds = inventory.StartingItems
            }),
            AbilityEntityComponentDefinition abilities => Result<EntityComponentState>.Success(new AbilityEntityComponentState
            {
                ComponentId = abilities.ComponentId,
                AbilityIds = abilities.AbilityIds
            }),
            _ => Result<EntityComponentState>.Failure($"Unknown entity component: {component.GetType().Name}")
        };
    }

    private Result<EntityComponentState> MaterializeResources(
        ResourceEntityComponentDefinition component,
        string ownerId,
        string contentRevision,
        string? configName)
    {
        var pools = new Dictionary<string, ResourcePool>(StringComparer.Ordinal);
        foreach (var (resourceId, configured) in component.Pools.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!float.IsFinite(configured.Current) || !float.IsFinite(configured.Max))
                return Result<EntityComponentState>.Failure($"Entity resource values must be finite: {resourceId}");

            Result<ResourcePool> created;
            if (_resources is IRevisionedResourceManager revisioned)
                created = revisioned.CreatePool(resourceId, configured.Current, contentRevision, configName);
            else
            {
                try { created = Result<ResourcePool>.Success(_resources.CreatePool(resourceId, configured.Current)); }
                catch (Exception exception) { created = Result<ResourcePool>.Failure(exception.Message, exception); }
            }
            if (created.IsFailure)
                return Result<EntityComponentState>.Failure(created.Error);
            pools[resourceId] = ResourcePool.Materialize(created.Value.Definition, configured.Current, configured.Max);
        }

        return Result<EntityComponentState>.Success(new ResourceEntityComponentState
        {
            ComponentId = component.ComponentId,
            State = new ResourceSet { OwnerId = ownerId, Resources = pools }
        });
    }
}
