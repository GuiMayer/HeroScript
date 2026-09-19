using Core.Combat.Models;
using Core.Run;

namespace Core.Combat;

public sealed record CombatRunEncounterResult
{
    public CombatState CombatState { get; init; } = new();
    public RunState RunState { get; init; } = new();
}

public sealed record CombatRunActionResult
{
    public CombatState CombatState { get; init; } = new();
    public RunState RunState { get; init; } = new();
    public string? ConsumedCardId { get; init; }
}
