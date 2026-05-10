namespace Core.Effects;

/// <summary>
/// Tipos de efeitos que podem ser executados em combate.
/// Effect é a unidade fundamental de todas as ações em combate.
/// </summary>
public enum EffectType
{
    // ===== RECURSOS =====
    /// <summary>
    /// Causa dano a um recurso (geralmente health)
    /// </summary>
    DAMAGE,
    
    /// <summary>
    /// Cura/restaura um recurso
    /// </summary>
    HEAL,
    
    /// <summary>
    /// Modifica qualquer recurso (energia, mana, stamina, etc.)
    /// </summary>
    MODIFY_RESOURCE,
    
    // ===== ECONOMIA =====
    /// <summary>
    /// Ganha ouro
    /// </summary>
    GAIN_GOLD,
    
    /// <summary>
    /// Perde ouro
    /// </summary>
    LOSE_GOLD,
    
    /// <summary>
    /// Ganha Power Points (usado para injetar modificadores)
    /// </summary>
    GAIN_PP,
    
    /// <summary>
    /// Perde Power Points
    /// </summary>
    LOSE_PP,
    
    // ===== STATUS =====
    /// <summary>
    /// Aplica status (buff/debuff/DoT/HoT)
    /// </summary>
    APPLY_STATUS,
    
    /// <summary>
    /// Remove status específico
    /// </summary>
    REMOVE_STATUS,
    
    /// <summary>
    /// Dispela tipos de status (ex: todos os debuffs)
    /// </summary>
    DISPEL_STATUS,
    
    // ===== CARTAS/DECK =====
    /// <summary>
    /// Compra carta do deck
    /// </summary>
    DRAW_CARD,
    
    /// <summary>
    /// Descarta carta da mão
    /// </summary>
    DISCARD_CARD,
    
    /// <summary>
    /// Exausta carta (remove da run)
    /// </summary>
    EXHAUST_CARD,
    
    /// <summary>
    /// Adiciona carta específica à mão
    /// </summary>
    ADD_CARD_TO_HAND,
    
    // ===== MODIFICADORES =====
    /// <summary>
    /// Modifica dano causado (buff de ataque)
    /// </summary>
    MODIFY_DAMAGE_DEALT,
    
    /// <summary>
    /// Modifica dano recebido (vulnerability)
    /// </summary>
    MODIFY_DAMAGE_TAKEN,
    
    /// <summary>
    /// Modifica chance de crítico
    /// </summary>
    MODIFY_CRIT_CHANCE,
    
    /// <summary>
    /// Modifica multiplicador de crítico
    /// </summary>
    MODIFY_CRIT_MULT,
    
    /// <summary>
    /// Modifica cooldowns de ações
    /// </summary>
    MODIFY_COOLDOWNS,
    
    // ===== CONTROLE =====
    /// <summary>
    /// Impede ações (stun, silence)
    /// </summary>
    PREVENT_ACTIONS,
    
    /// <summary>
    /// Força alvo específico (taunt)
    /// </summary>
    FORCE_TARGET,
    
    /// <summary>
    /// Pula turno da entidade
    /// </summary>
    SKIP_TURN,
    
    // ===== UTILIDADE =====
    /// <summary>
    /// Reflete dano recebido de volta ao atacante
    /// </summary>
    REFLECT_DAMAGE,
    
    /// <summary>
    /// Absorve dano antes de afetar HP (shield/barrier)
    /// </summary>
    ABSORB_DAMAGE,
    
    /// <summary>
    /// Dispara outro effect (chain/trigger)
    /// </summary>
    TRIGGER_EFFECT,
    
    /// <summary>
    /// Effect condicional (if/then)
    /// </summary>
    CONDITIONAL_EFFECT,
    
    // ===== META =====
    /// <summary>
    /// Modifica outro effect (meta-effect)
    /// </summary>
    MODIFY_EFFECT,
    
    /// <summary>
    /// Copia effect de outra fonte
    /// </summary>
    COPY_EFFECT
}

/// <summary>
/// Alvo do efeito
/// </summary>
public enum EffectTarget
{
    /// <summary>
    /// Quem executou a ação
    /// </summary>
    SELF,
    
    /// <summary>
    /// Alvo selecionado
    /// </summary>
    TARGET,
    
    /// <summary>
    /// Todos os inimigos
    /// </summary>
    ALL_ENEMIES,
    
    /// <summary>
    /// Todos os aliados (futuro)
    /// </summary>
    ALL_ALLIES,
    
    /// <summary>
    /// Inimigo aleatório
    /// </summary>
    RANDOM_ENEMY,
    
    /// <summary>
    /// Inimigo com menor HP
    /// </summary>
    LOWEST_HP_ENEMY,
    
    /// <summary>
    /// Inimigo com maior HP
    /// </summary>
    HIGHEST_HP_ENEMY
}

/// <summary>
/// Timing de execução do efeito
/// </summary>
public enum EffectTiming
{
    /// <summary>
    /// Executa imediatamente
    /// </summary>
    IMMEDIATE,
    
    /// <summary>
    /// Executa após X turnos
    /// </summary>
    DELAYED,
    
    /// <summary>
    /// Executa no início do turno
    /// </summary>
    ON_TURN_START,
    
    /// <summary>
    /// Executa no fim do turno
    /// </summary>
    ON_TURN_END,
    
    /// <summary>
    /// Executa ao causar dano
    /// </summary>
    ON_DAMAGE_DEALT,
    
    /// <summary>
    /// Executa ao receber dano
    /// </summary>
    ON_DAMAGE_TAKEN
}

/// <summary>
/// Estado de execução do effect
/// </summary>
public enum EffectExecutionState
{
    /// <summary>
    /// Aguardando execução
    /// </summary>
    PENDING,
    
    /// <summary>
    /// Em execução
    /// </summary>
    EXECUTING,
    
    /// <summary>
    /// Executado com sucesso
    /// </summary>
    COMPLETED,
    
    /// <summary>
    /// Falhou
    /// </summary>
    FAILED,
    
    /// <summary>
    /// Cancelado
    /// </summary>
    CANCELLED
}
