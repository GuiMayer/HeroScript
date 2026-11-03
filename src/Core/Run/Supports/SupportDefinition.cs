using System.Collections.Immutable;
using Core.Common;
using Core.Content;
using Core.Run.DeckLoadouts;
using Core.Run.Loadouts;

namespace Core.Run.Supports;

/// <summary>Equipment selects an ordinary automatic participant and its existing decision rules.</summary>
public sealed record SupportDefinition
{
    public string SupportId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonRequired]
    public bool CountsForOutcome { get; init; }
    public int OwnerActivationInterval { get; init; }
    public string EntityDefinitionId { get; init; } = string.Empty;
    public ImmutableArray<string> RoutineIds { get; init; } = [];
    public string DefaultRoutineId { get; init; } = string.Empty;
    public Result Validate(ContentRuntime runtime)
    {
        if (OwnerActivationInterval is < 1 or > 10000 || !BuildDefinitionRules.Symbol(SupportId) || !BuildDefinitionRules.Symbol(EntityDefinitionId) ||
            RoutineIds.IsDefaultOrEmpty || !BuildDefinitionRules.Symbols(RoutineIds) || !RoutineIds.Contains(DefaultRoutineId))
            return Result.Failure("Support requires identity, entity and an allowed default routine");
        if (!runtime.GetDefinitions("entities").ContainsKey(EntityDefinitionId)) return Result.Failure($"Missing entity: {EntityDefinitionId}");
        foreach (var id in RoutineIds)
        {
            var routine = runtime.GetDefinition<SupportRoutineDefinition>("support-routines", id);
            if (routine.IsFailure) return Result.Failure(routine.Error);
            var valid = routine.Value.Validate(runtime);
            if (valid.IsFailure) return valid;
        }
        return Result.Success();
    }
}

/// <summary>Cadence/effects live in the entity's ordinary resources, responses, actions and gambits.</summary>
public sealed record SupportRoutineDefinition
{
    public string RoutineId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ImmutableArray<string> GambitIds { get; init; } = [];
    public Result Validate(ContentRuntime runtime)
    {
        if (!BuildDefinitionRules.Symbol(RoutineId) || GambitIds.IsDefaultOrEmpty || !BuildDefinitionRules.Symbols(GambitIds))
            return Result.Failure("Routine requires identity and unique gambits");
        foreach (var id in GambitIds)
            if (!runtime.GetDefinitions("gambits").ContainsKey(id)) return Result.Failure($"Missing gambit: {id}");
        return Result.Success();
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SupportInventoryScope { Actor, Run }

public sealed record SupportLoadoutPolicyDefinition
{
    public string PolicyId { get; init; } = string.Empty;
    public ImmutableArray<LoadoutEquipmentWindow> EditWindows { get; init; } = [];
    public ImmutableArray<string> SupportIds { get; init; } = [];
    public string? StartingSupportId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LoadoutRevisionMigration? RevisionMigration { get; init; }
    public SupportInventoryScope InventoryScope { get; init; }
    public ImmutableArray<string> StartingOwnedSupportIds { get; init; } = [];
    public Result Validate(ContentRuntime runtime)
    {
        if (RevisionMigration.HasValue && !Enum.IsDefined(RevisionMigration.Value) || !Enum.IsDefined(InventoryScope) || StartingOwnedSupportIds.IsDefault || !BuildDefinitionRules.Symbols(StartingOwnedSupportIds) ||
            StartingOwnedSupportIds.Any(id => !SupportIds.Contains(id)) || StartingSupportId != null && !StartingOwnedSupportIds.Contains(StartingSupportId) ||
            !BuildDefinitionRules.Symbol(PolicyId) || SupportIds.IsDefaultOrEmpty || !BuildDefinitionRules.Symbols(SupportIds) ||
            EditWindows.IsDefaultOrEmpty || EditWindows.Any(w => !Enum.IsDefined(w) || w == LoadoutEquipmentWindow.Combat) ||
            EditWindows.Distinct().Count() != EditWindows.Length || StartingSupportId != null && !SupportIds.Contains(StartingSupportId))
            return Result.Failure("Support policy requires explicit supported windows and eligible equipment");
        foreach (var id in SupportIds)
        {
            var support = runtime.GetDefinition<SupportDefinition>("supports", id);
            if (support.IsFailure) return Result.Failure(support.Error);
            var valid = support.Value.Validate(runtime);
            if (valid.IsFailure) return valid;
        }
        return Result.Success();
    }
}
