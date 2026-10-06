using System.Collections.Immutable;
using Core.Resources;

namespace Core.Run;

public sealed record PreparationDefinition
{
    private ImmutableList<PreparationOptionDefinition> _options = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;

    public string PreparationId { get; init; } = string.Empty;
    public IReadOnlyList<PreparationOptionDefinition> Options
    {
        get => _options;
        init => _options = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record PreparationOptionDefinition
{
    public ImmutableArray<Core.Effects.EffectDefinition> Effects { get; init; } = [];
    private ImmutableList<string> _grantedCardIds = [];
    private ImmutableList<PreparationModifierGrantDefinition> _applyModifiers = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;
    private ImmutableArray<ResourceAmount> _costs = [];

    public string OptionId { get; init; } = string.Empty;
    public IReadOnlyList<ResourceAmount> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> GrantedCardIds
    {
        get => _grantedCardIds;
        init => _grantedCardIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<PreparationModifierGrantDefinition> ApplyModifiers
    {
        get => _applyModifiers;
        init => _applyModifiers = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record PreparationModifierGrantDefinition
{
    public string OwnerId { get; init; } = "run";
    public string ModifierId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
    public int? Duration { get; init; }
    public string? SourceId { get; init; }
}

public sealed record PreparationState
{
    private ImmutableList<PreparationOptionState> _options = [];
    private ImmutableList<string> _appliedOptionIds = [];

    public Guid PreparationInstanceId { get; init; }
    public Guid RunId { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public string PreparationId { get; init; } = string.Empty;
    public IReadOnlyList<PreparationOptionState> Options
    {
        get => _options;
        init => _options = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<string> AppliedOptionIds
    {
        get => _appliedOptionIds;
        init => _appliedOptionIds = value?.ToImmutableList() ?? [];
    }
}

public sealed record PreparationOptionState
{
    public ImmutableArray<Core.Effects.EffectDefinition> Effects { get; init; } = [];
    private ImmutableList<string> _grantedCardIds = [];
    private ImmutableList<PreparationModifierGrantState> _applyModifiers = [];
    private ImmutableList<Guid> _appliedModifierInstanceIds = [];
    private ImmutableArray<ResourceAmount> _costs = [];

    public string OptionId { get; init; } = string.Empty;
    public IReadOnlyList<ResourceAmount> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> GrantedCardIds
    {
        get => _grantedCardIds;
        init => _grantedCardIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<PreparationModifierGrantState> ApplyModifiers
    {
        get => _applyModifiers;
        init => _applyModifiers = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<Guid> AppliedModifierInstanceIds
    {
        get => _appliedModifierInstanceIds;
        init => _appliedModifierInstanceIds = value?.ToImmutableList() ?? [];
    }
    public bool Applied { get; init; }
}

public sealed record PreparationModifierGrantState
{
    public string OwnerId { get; init; } = "run";
    public string ModifierId { get; init; } = string.Empty;
    public int Stacks { get; init; } = 1;
    public int? Duration { get; init; }
    public string? SourceId { get; init; }
}
