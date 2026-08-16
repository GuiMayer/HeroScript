namespace Core.Combat.Models;

/// <summary>
/// Representa uma ação executada em combate.
/// Imutável.
/// </summary>
public record CombatAction
{
    public Guid ActionId { get; init; } = Guid.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UnixEpoch;
    public int Turn { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }  // Null para BASIC_ATTACK, PASS, END_TURN
    public string? TargetId { get; init; }  // Null para PASS, END_TURN
    public int? DamageDealt { get; init; }  // Resultado da ação
    public int? EnergyChange { get; init; }  // +1 para BASIC_ATTACK, -X para POWER
}
