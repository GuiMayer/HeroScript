using Core.Common;

namespace Core.Entity.Definitions;

public static class EntityDefinitionValidator
{
    public static Result Validate(EntityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.DefinitionId))
            return Result.Failure("DefinitionId is required");
        if (string.IsNullOrWhiteSpace(definition.DisplayName))
            return Result.Failure("DisplayName is required");
        if (definition.Components.Count == 0)
            return Result.Failure("At least one entity component is required");
        if (definition.Components.Any(component => component == null || string.IsNullOrWhiteSpace(component.ComponentId)))
            return Result.Failure("Every entity component requires a componentId");

        var duplicate = definition.Components
            .GroupBy(component => component.ComponentId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            return Result.Failure($"Duplicate entity componentId: {duplicate.Key}");
        if (definition.Components.OfType<ResourceEntityComponentDefinition>().Count() > 1)
            return Result.Failure("An entity may define only one resources component");

        foreach (var component in definition.Components)
        {
            switch (component)
            {
                case ResourceEntityComponentDefinition resources:
                    foreach (var (resourceId, pool) in resources.Pools)
                    {
                        if (string.IsNullOrWhiteSpace(resourceId) || !float.IsFinite(pool.Current) ||
                            !float.IsFinite(pool.Max) || pool.Max < 0)
                            return Result.Failure($"Invalid entity resource pool: {resourceId}");
                    }
                    break;
                case StatEntityComponentDefinition stats when
                    stats.Values.Any(value => string.IsNullOrWhiteSpace(value.Key) || !float.IsFinite(value.Value)):
                    return Result.Failure("Entity stats require non-empty IDs and finite values");
                case InventoryEntityComponentDefinition inventory when inventory.Capacity < -1:
                    return Result.Failure("Entity inventory capacity must be -1 or non-negative");
                case InventoryEntityComponentDefinition inventory when
                    HasInvalidOrDuplicateIds(inventory.StartingItems):
                    return Result.Failure("Entity inventory item IDs must be non-empty and unique");
                case AbilityEntityComponentDefinition abilities when
                    HasInvalidOrDuplicateIds(abilities.AbilityIds):
                    return Result.Failure("Entity ability IDs must be non-empty and unique");
                case not ResourceEntityComponentDefinition and
                    not StatEntityComponentDefinition and
                    not InventoryEntityComponentDefinition and
                    not AbilityEntityComponentDefinition:
                    return Result.Failure($"Unsupported entity component type: {component.GetType().Name}");
            }
        }
        return Result.Success();
    }

    private static bool HasInvalidOrDuplicateIds(IEnumerable<string> ids)
    {
        var materialized = ids.ToArray();
        return materialized.Any(string.IsNullOrWhiteSpace) ||
               materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length;
    }
}
