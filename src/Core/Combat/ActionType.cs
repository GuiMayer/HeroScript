namespace Core.Combat;

/// <summary>
/// Tipos de ação disponíveis em combate.
/// </summary>
public enum ActionType
{
    /// <summary>
    /// Ataque básico (gera energia).
    /// </summary>
    BASIC_ATTACK,
    
    /// <summary>
    /// Usar poder (consome energia).
    /// </summary>
    POWER,
    
    /// <summary>
    /// Passar turno sem ação.
    /// </summary>
    PASS,
    
    /// <summary>
    /// Finalizar turno.
    /// </summary>
    END_TURN
}
