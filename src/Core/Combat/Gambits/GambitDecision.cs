using Core.Combat.Models;

namespace Core.Combat.Gambits;

public sealed record GambitDecision
{
    public EntityAction Action { get; init; } = new();
    public string? GambitId { get; init; }
    public int Priority { get; init; }
    public GambitIntentDefinition Intent { get; init; } = new();
}

public sealed record EntityAction
{
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public int? CostOptionId { get; init; }
}
