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
    public string EntityDefinitionId { get; init; } = string.Empty;
    public ImmutableArray<string> RoutineIds { get; init; } = [];
    public string DefaultRoutineId { get; init; } = string.Empty;
    public Result Validate(ContentRuntime runtime)
    {
        if (!BuildDefinitionRules.Symbol(SupportId) || !BuildDefinitionRules.Symbol(EntityDefinitionId) ||
            RoutineIds.IsDefaultOrEmpty || !BuildDefinitionRules.Symbols(RoutineIds) || !RoutineIds.Contains(DefaultRoutineId))
            return Result.Failure("Support requires identity, entity and an allowed default routine");
        if (!runtime.GetDefinitions("entities").ContainsKey(EntityDefinitionId)) return Result.Failure($"Missing entity: {EntityDefinitionId}");
        foreach (var id in RoutineIds)
            if (!runtime.GetDefinitions("support-routines").ContainsKey(id)) return Result.Failure($"Missing routine: {id}");
        return Result.Success();
    }
}

/// <summary>Cadence/effects live in the entity's ordinary resources, responses, actions and gambits.</summary>
public sealed record SupportRoutineDefinition
{
    public string RoutineId { get; init; } = string.Empty;
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

public sealed record SupportLoadoutPolicyDefinition
{
    public string PolicyId { get; init; } = string.Empty;
    public ImmutableArray<LoadoutEquipmentWindow> EditWindows { get; init; } = [];
    public ImmutableArray<string> SupportIds { get; init; } = [];
    public string? StartingSupportId { get; init; }
    public Result Validate(ContentRuntime runtime)
    {
        if (!BuildDefinitionRules.Symbol(PolicyId) || SupportIds.IsDefaultOrEmpty || !BuildDefinitionRules.Symbols(SupportIds) ||
            EditWindows.IsDefaultOrEmpty || EditWindows.Any(w => !Enum.IsDefined(w) || w == LoadoutEquipmentWindow.Combat) ||
            EditWindows.Distinct().Count() != EditWindows.Length || StartingSupportId != null && !SupportIds.Contains(StartingSupportId))
            return Result.Failure("Support policy requires explicit supported windows and eligible equipment");
        foreach (var id in SupportIds)
            if (!runtime.GetDefinitions("supports").ContainsKey(id)) return Result.Failure($"Missing support: {id}");
        return Result.Success();
    }
}
