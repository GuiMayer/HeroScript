using Core.Combat.Models;

namespace Core.Combat.Intents;

public sealed record CombatIntent
{
    public string ActorId { get; init; } = string.Empty;
    public ActionType ActionType { get; init; } = ActionType.PASS;
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public string? CostOptionId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TelegraphType { get; init; } = "Unknown";
    public float? EstimatedDamage { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public int Priority { get; init; }
    public string? SourceGambitId { get; init; }
}
