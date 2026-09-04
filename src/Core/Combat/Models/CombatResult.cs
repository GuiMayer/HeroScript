namespace Core.Combat.Models;

/// <summary>
/// Resultado final de um combate.
/// </summary>
public record CombatResult
{
    public Guid CombatId { get; init; }
    public CombatStatus Status { get; init; }
    public int TotalTurns { get; init; }
    public int TotalActions { get; init; }
    public TimeSpan Duration { get; init; }
}
