namespace Core.Combat.TurnPhase;

/// <summary>
/// Fases disponíveis dentro de um turno.
/// Suporta múltiplos estilos de TCG (Magic, Yu-Gi-Oh!, Hearthstone, etc.)
/// </summary>
public enum TurnPhase
{
    /// <summary>
    /// Sem sistema de fases (retrocompatibilidade com sistema atual)
    /// </summary>
    NONE,
    
    /// <summary>
    /// Fase de desvira/desengaja permanentes (Magic: Untap)
    /// </summary>
    UNTAP,
    
    /// <summary>
    /// Fase de manutenção/preparação (Magic: Upkeep)
    /// </summary>
    UPKEEP,
    
    /// <summary>
    /// Fase de compra de cartas
    /// </summary>
    DRAW,
    
    /// <summary>
    /// Fase de espera/preparação (Yu-Gi-Oh!: Standby Phase)
    /// </summary>
    STANDBY,
    
    /// <summary>
    /// Fase principal 1 - jogadas principais antes do combate
    /// </summary>
    MAIN_1,
    
    /// <summary>
    /// Início da fase de batalha
    /// </summary>
    BATTLE_START,
    
    /// <summary>
    /// Declaração de atacantes
    /// </summary>
    BATTLE_DECLARE_ATTACKERS,
    
    /// <summary>
    /// Declaração de bloqueadores/defensores
    /// </summary>
    BATTLE_DECLARE_BLOCKERS,
    
    /// <summary>
    /// Resolução de dano de combate
    /// </summary>
    BATTLE_DAMAGE,
    
    /// <summary>
    /// Fim da fase de batalha
    /// </summary>
    BATTLE_END,
    
    /// <summary>
    /// Fase principal 2 - jogadas principais após o combate
    /// </summary>
    MAIN_2,
    
    /// <summary>
    /// Fase final do turno
    /// </summary>
    END,
    
    /// <summary>
    /// Fase de limpeza (Magic: Cleanup)
    /// Descarte até tamanho máximo de mão, remove efeitos "até o fim do turno"
    /// </summary>
    CLEANUP
}
