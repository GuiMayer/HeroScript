namespace Core.Combat.Models;

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
    END_TURN,
    
    // ===== Ações do Sistema de Fases TCG =====
    
    /// <summary>
    /// Transicionar para a próxima fase do turno.
    /// Usado em sistemas TCG com múltiplas fases (Magic, Yu-Gi-Oh!, etc.)
    /// </summary>
    TRANSITION_PHASE,
    
    /// <summary>
    /// Passar prioridade para o próximo jogador.
    /// Usado em sistemas TCG com prioridade interativa.
    /// </summary>
    PASS_PRIORITY,
    
    /// <summary>
    /// Declarar uma entidade como atacante na fase de combate.
    /// Usado em TCGs com fase de declaração de atacantes (Magic, etc.)
    /// </summary>
    DECLARE_ATTACKER,
    
    /// <summary>
    /// Declarar uma entidade como bloqueador/defensor na fase de combate.
    /// Usado em TCGs com fase de declaração de bloqueadores (Magic, etc.)
    /// </summary>
    DECLARE_BLOCKER,
    
    /// <summary>
    /// Jogar uma ação instantânea que pode ser respondida.
    /// Usado em TCGs com pilha de resolução (Magic: Instant, Yu-Gi-Oh!: Quick-Play, etc.)
    /// </summary>
    PLAY_INSTANT,
    
    /// <summary>
    /// Ativar uma habilidade de uma entidade ou permanente.
    /// Usado em TCGs com habilidades ativadas (Magic: Activated Abilities, etc.)
    /// </summary>
    ACTIVATE_ABILITY
}
