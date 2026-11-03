using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Content;
using Core.Run.Loadouts;

namespace Core.Run.DeckLoadouts;

/// <summary>Persistent build selection, independent from zone circulation and component grants.</summary>
public sealed record DeckCompositionPolicyDefinition
{
    public string PolicyId { get; init; } = string.Empty;
    public ImmutableArray<LoadoutEquipmentWindow> EditWindows { get; init; } = [];
    public ImmutableArray<DeckCompositionGroupDefinition> Groups { get; init; } = [];

    public Result Validate(ContentRuntime runtime)
    {
        if (!BuildDefinitionRules.Symbol(PolicyId) || Groups.IsDefaultOrEmpty || Groups.Length > 32 ||
            Groups.Any(g => g == null) || Groups.Select(g => g.GroupId).Distinct(StringComparer.Ordinal).Count() != Groups.Length ||
            EditWindows.IsDefaultOrEmpty || EditWindows.Any(w => !Enum.IsDefined(w) || w == LoadoutEquipmentWindow.Combat) ||
            EditWindows.Distinct().Count() != EditWindows.Length)
            return Result.Failure("Composition requires an identity, unique groups and supported explicit edit windows");
        if (Groups.Sum(g => (long)g.Maximum) > 512)
            return Result.Failure("Composition exceeds the technical card budget");
        foreach (var group in Groups)
        {
            var valid = group.Validate(runtime);
            if (valid.IsFailure) return Result.Failure($"groups/{group.GroupId}: {valid.Error}");
        }
        return Result.Success();
    }
}

public sealed record DeckCompositionGroupDefinition
{
    public string GroupId { get; init; } = string.Empty;
    [JsonRequired] public int Minimum { get; init; }
    [JsonRequired] public int Maximum { get; init; }
    [JsonRequired] public bool AllowDuplicateDefinitions { get; init; }
    public ImmutableArray<string> CardIds { get; init; } = [];
    public ImmutableArray<string> StartingCardIds { get; init; } = [];
    public ImmutableArray<string> ModuleIds { get; init; } = [];
    public string? StartingModuleId { get; init; }

    public Result Validate(ContentRuntime runtime)
    {
        if (!BuildDefinitionRules.Symbol(GroupId) || Minimum < 0 || Maximum < Minimum || Maximum > 512 ||
            !BuildDefinitionRules.Symbols(CardIds) || !BuildDefinitionRules.Symbols(ModuleIds) ||
            StartingCardIds.IsDefault || StartingCardIds.Any(id => !BuildDefinitionRules.Symbol(id)))
            return Result.Failure("Invalid identity, bounds or card/module identities");
        if (CardIds.IsEmpty == ModuleIds.IsEmpty)
            return Result.Failure("Specify exactly one eligibility source: cardIds or moduleIds");
        if (!CardIds.IsEmpty)
        {
            if (StartingModuleId != null) return Result.Failure("Card group cannot select a module");
            foreach (var id in CardIds)
                if (!runtime.GetDefinitions("cards").ContainsKey(id)) return Result.Failure($"Missing card: {id}");
            return ValidateSelection(StartingCardIds, CardIds);
        }
        if (!StartingCardIds.IsEmpty || StartingModuleId == null || !ModuleIds.Contains(StartingModuleId))
            return Result.Failure("Module group requires an allowed starting module and no direct starting cards");
        foreach (var id in ModuleIds)
        {
            var module = runtime.GetDefinition<DeckModuleDefinition>("deck-modules", id);
            if (module.IsFailure) return Result.Failure(module.Error);
            var valid = module.Value.Validate(runtime);
            if (valid.IsFailure) return valid;
            valid = ValidateSelection(module.Value.StartingCardIds, module.Value.CardIds);
            if (valid.IsFailure) return Result.Failure($"module {id}: {valid.Error}");
        }
        return Result.Success();
    }

    internal Result ValidateSelection(IReadOnlyList<string> selected, IReadOnlyList<string> allowed)
    {
        if (selected.Count < Minimum || selected.Count > Maximum || selected.Any(id => !allowed.Contains(id)))
            return Result.Failure("Starting selection does not satisfy group bounds/eligibility");
        if (!AllowDuplicateDefinitions && selected.Distinct(StringComparer.Ordinal).Count() != selected.Count)
            return Result.Failure("Duplicate card definitions are not allowed in this group");
        return Result.Success();
    }
}

public sealed record DeckModuleDefinition
{
    public string ModuleId { get; init; } = string.Empty;
    public ImmutableArray<string> CardIds { get; init; } = [];
    public ImmutableArray<string> StartingCardIds { get; init; } = [];
    public Result Validate(ContentRuntime runtime)
    {
        if (!BuildDefinitionRules.Symbol(ModuleId) || CardIds.IsDefaultOrEmpty || !BuildDefinitionRules.Symbols(CardIds) ||
            StartingCardIds.IsDefaultOrEmpty || StartingCardIds.Length > 512 || StartingCardIds.Any(id => !CardIds.Contains(id)))
            return Result.Failure("Module requires an identity, eligible cards and a nonempty starting selection");
        foreach (var id in CardIds)
            if (!runtime.GetDefinitions("cards").ContainsKey(id)) return Result.Failure($"Missing card: {id}");
        return Result.Success();
    }
}

internal static class BuildDefinitionRules
{
    internal static bool Symbol(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
    internal static bool Symbols(ImmutableArray<string> values) => !values.IsDefault && values.Length <= 512 &&
        values.All(Symbol) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
}
