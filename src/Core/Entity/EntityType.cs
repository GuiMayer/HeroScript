namespace Core.Entity;

/// <summary>
/// Tipo de entidade no jogo
/// </summary>
public enum EntityType
{
    /// <summary>
    /// Jogador controlado pelo usuário
    /// </summary>
    PLAYER,
    
    /// <summary>
    /// Companion controlado por Gambits
    /// </summary>
    COMPANION,
    
    /// <summary>
    /// Inimigo controlado por IA
    /// </summary>
    ENEMY,
    
    /// <summary>
    /// NPC não-combatente
    /// </summary>
    NPC
}
