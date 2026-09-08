using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Combat.Models;

namespace Core.Combat.Gambits;

/// <summary>A pinned rule that only filters and ranks already-legal candidates.</summary>
public sealed record GambitDefinition
{
    private ImmutableArray<DecisionPredicateDefinition> _predicates = [];
    private ImmutableArray<string> _tags = [];

    public string GambitId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Priority { get; init; }
    public IReadOnlyList<DecisionPredicateDefinition> Predicates
    {
        get => _predicates;
        init => _predicates = value?.ToImmutableArray() ?? [];
    }
    public GambitActionMatcher Action { get; init; } = new();
    public GambitIntentDefinition Intent { get; init; } = new();
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? [];
    }
}

public sealed record GambitIntentDefinition
{
    private ImmutableArray<string> _tags = [];

    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? TelegraphType { get; init; }
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? [];
    }
}

/// <summary>
/// Formula-backed predicate over the generic decision variable map. Bounds are
/// inclusive and the expression is handled by the shared math runtime.
/// </summary>
public sealed record DecisionPredicateDefinition
{
    public string Expression { get; init; } = "1";
    public float? Minimum { get; init; }
    public float? Maximum { get; init; }
}

public sealed record GambitActionMatcher
{
    public ActionType? ActionType { get; init; }
    public string? ActionId { get; init; }
    public string? CardDefinitionId { get; init; }
    public DecisionTargetSelectorDefinition TargetSelector { get; init; } = new();
    public string? CostOptionId { get; init; }
}

public sealed record DecisionTargetSelectorDefinition
{
    public DecisionTargetSelection Strategy { get; init; } = DecisionTargetSelection.None;
    public SideRelationship? Relationship { get; init; }
    public string? ResourceId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionTargetSelection
{
    None,
    Self,
    FirstOrdinal,
    LowestResource,
    HighestResource
}
