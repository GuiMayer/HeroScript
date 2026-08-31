using System.Collections.Immutable;
using Core.Combat.Models;

namespace Core.Combat.Gambits;

public record GambitDefinition
{
    private ImmutableArray<GambitCondition> _conditions = ImmutableArray<GambitCondition>.Empty;
    private ImmutableArray<string> _tags = ImmutableArray<string>.Empty;

    public string GambitId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Priority { get; init; }
    public IReadOnlyList<GambitCondition> Conditions
    {
        get => _conditions;
        init => _conditions = value?.ToImmutableArray() ?? ImmutableArray<GambitCondition>.Empty;
    }
    public GambitActionDefinition Action { get; init; } = new();
    public GambitIntentDefinition Intent { get; init; } = new();
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }
}

public record GambitIntentDefinition
{
    private ImmutableArray<string> _tags = ImmutableArray<string>.Empty;

    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? TelegraphType { get; init; }
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }
}

public record GambitCondition
{
    public GambitConditionType Type { get; init; } = GambitConditionType.ALWAYS;
    public string? Target { get; init; }
    public string? ResourceId { get; init; }
    public float? LessThanOrEqual { get; init; }
    public float? GreaterThanOrEqual { get; init; }
}

public record GambitActionDefinition
{
    public ActionType ActionType { get; init; } = ActionType.PASS;
    public string? PowerId { get; init; }
    public string? Target { get; init; }
    public int? CostOptionId { get; init; }
}

public enum GambitConditionType
{
    ALWAYS,
    SELF_RESOURCE_PERCENT,
    ACTOR_RESOURCE_PERCENT,
    HERO_RESOURCE_PERCENT,
    TARGET_RESOURCE_PERCENT,
    ANY_ENEMY_ALIVE,
    TURN_GREATER_THAN_OR_EQUAL
}
