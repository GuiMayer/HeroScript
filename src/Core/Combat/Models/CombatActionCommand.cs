namespace Core.Combat.Models;

/// <summary>
/// Command submitted by any controller type to execute an action for a combat actor.
/// </summary>
public sealed record CombatActionCommand
{
    public string ActorId { get; init; } = string.Empty;
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public string? CostOptionId { get; init; }
    public Guid? RunId { get; init; }
    public string? CardId { get; init; }
}
