namespace Core.Effects;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum EffectChanceScope { PerEffect, PerTarget }

/// <summary>
/// Tipos de efeitos que podem ser executados em combate.
/// Effect é a unidade fundamental de todas as ações em combate.
/// </summary>
public enum EffectType
{
    // ===== RECURSOS =====
    /// <summary>
    /// Subtrai do recurso explicitamente selecionado pelo efeito.
    /// </summary>
    DAMAGE,
    
    /// <summary>
    /// Adiciona ao recurso explicitamente selecionado pelo efeito.
    /// </summary>
    HEAL,
    
    /// <summary>
    /// Modifica qualquer campo de um recurso com uma operação explícita.
    /// </summary>
    MODIFY_RESOURCE,
    
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

    APPLY_MODIFIER,
    REMOVE_MODIFIER
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
    /// Inimigo com o menor valor no recurso de seleção configurado
    /// </summary>
    LOWEST_RESOURCE_ENEMY,
    
    /// <summary>
    /// Inimigo com o maior valor no recurso de seleção configurado
    /// </summary>
    HIGHEST_RESOURCE_ENEMY
}
