namespace Core.Combat.Models;

/// <summary>
/// Status do combate.
/// </summary>
public enum CombatStatus
{
    /// <summary>
    /// Combate em andamento.
    /// </summary>
    ACTIVE,
    
    /// <summary>
    /// Herói venceu (todos inimigos mortos).
    /// </summary>
    VICTORY,
    
    /// <summary>
    /// Herói derrotado.
    /// </summary>
    DEFEAT,

    /// <summary>
    /// Todos os lados satisfizeram a condição terminal na mesma fronteira.
    /// </summary>
    DRAW,
    
    /// <summary>
    /// Combate abandonado.
    /// </summary>
    ABANDONED
}
