using Core.Entity.Controllers;

namespace Core.Combat.Gambits;

public sealed record GambitDecision
{
    public EntityAction Action { get; init; } = new();
    public string? GambitId { get; init; }
    public int Priority { get; init; }
    public GambitIntentDefinition Intent { get; init; } = new();
}
