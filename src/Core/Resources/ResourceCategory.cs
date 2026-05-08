namespace Core.Resources;

/// <summary>
/// Categorias de recursos no sistema.
/// </summary>
public enum ResourceCategory
{
    /// <summary>
    /// Recursos vitais - morte se chegar a 0 (HP, Shield)
    /// </summary>
    VITAL,
    
    /// <summary>
    /// Recursos táticos - consumo/ganho em combate (Energy, Mana, Stamina)
    /// </summary>
    TACTICAL,
    
    /// <summary>
    /// Recursos especiais - mecânicas únicas (Rage, Combo, Momentum)
    /// </summary>
    SPECIAL,
    
    /// <summary>
    /// Recursos temporários - buffs com duração
    /// </summary>
    TEMPORARY
}
