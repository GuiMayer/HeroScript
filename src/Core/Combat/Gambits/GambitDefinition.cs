using Core.Combat.Models;

namespace Core.Combat.Gambits;

public record GambitDefinition
{
    public string GambitId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Priority { get; init; }
    public List<GambitCondition> Conditions { get; init; } = new();
    public GambitActionDefinition Action { get; init; } = new();
    public List<string> Tags { get; init; } = new();
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
    HERO_RESOURCE_PERCENT,
    TARGET_RESOURCE_PERCENT,
    ANY_ENEMY_ALIVE,
    TURN_GREATER_THAN_OR_EQUAL
}
