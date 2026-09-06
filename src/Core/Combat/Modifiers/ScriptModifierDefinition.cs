using System.Collections.Immutable;
using Core.Calculations;
using Core.Effects;

namespace Core.Combat.Modifiers;

/// <summary>
/// Data-driven modifier definition loaded from JSON.
/// </summary>
public record ScriptModifierDefinition
{
    private ImmutableArray<string> _requiredTags = ImmutableArray<string>.Empty;
    private ImmutableArray<string> _excludedTags = ImmutableArray<string>.Empty;
    private ImmutableArray<string> _tags = ImmutableArray<string>.Empty;
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableArray<ContextualInfluenceDefinition> _influences = [];

    public string ModifierId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ModifierKey { get; init; } = string.Empty;
    public string? FormulaValue { get; init; }
    public float BaseValue { get; init; }
    public int DefaultStacks { get; init; } = 1;
    public int MaxStacks { get; init; } = 99;
    public int DefaultDuration { get; init; } = -1;
    public StackReapplyPolicy Stacking { get; init; } = StackReapplyPolicy.Add;
    public DurationReapplyPolicy DurationReapply { get; init; } = DurationReapplyPolicy.Preserve;
    public ModifierDurationBoundary DurationBoundary { get; init; } = ModifierDurationBoundary.Combat;
    public IReadOnlyList<string> RequiredTags
    {
        get => _requiredTags;
        init => _requiredTags = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    public IReadOnlyList<string> ExcludedTags
    {
        get => _excludedTags;
        init => _excludedTags = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    public IReadOnlyList<ContextualInfluenceDefinition> Influences
    {
        get => _influences;
        init => _influences = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
