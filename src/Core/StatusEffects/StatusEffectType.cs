namespace Core.StatusEffects;

/// <summary>
/// Tipos de status effects que podem ser aplicados em combate.
/// Status effects são efeitos temporários ou permanentes que modificam o comportamento de entidades.
/// </summary>
public enum StatusEffectType
{
    // ===== DAMAGE OVER TIME (DoTs) =====
    
    /// <summary>
    /// Causa dano de fogo por turno
    /// </summary>
    BURNING,
    
    /// <summary>
    /// Causa dano de veneno por turno
    /// </summary>
    POISON,
    
    /// <summary>
    /// Causa dano físico por turno (sangramento)
    /// </summary>
    BLEEDING,
    
    // ===== BUFFS (Efeitos Positivos) =====
    
    /// <summary>
    /// Aumenta dano causado
    /// </summary>
    STRENGTH,
    
    /// <summary>
    /// Aumenta chance de crítico
    /// </summary>
    DEXTERITY,
    
    /// <summary>
    /// Aumenta HP máximo temporariamente
    /// </summary>
    VIGOR,
    
    /// <summary>
    /// Cura HP por turno
    /// </summary>
    REGENERATION,
    
    /// <summary>
    /// Absorve dano antes de afetar HP
    /// </summary>
    SHIELD,
    
    /// <summary>
    /// Reflete dano recebido de volta ao atacante
    /// </summary>
    THORNS,
    
    // ===== DEBUFFS (Efeitos Negativos) =====
    
    /// <summary>
    /// Reduz dano causado
    /// </summary>
    WEAKNESS,
    
    /// <summary>
    /// Aumenta dano recebido
    /// </summary>
    VULNERABLE,
    
    /// <summary>
    /// Reduz block/defesa
    /// </summary>
    FRAIL,
    
    // ===== CONTROLE =====
    
    /// <summary>
    /// Não pode agir (pula turno)
    /// </summary>
    STUNNED,
    
    /// <summary>
    /// Não pode usar poderes (apenas ataque básico)
    /// </summary>
    SILENCED,
    
    /// <summary>
    /// Não pode se mover (futuro - para jogos com movimento)
    /// </summary>
    ROOTED,
    
    // ===== ESPECIAIS (Slay the Spire) =====
    
    /// <summary>
    /// Previne o próximo debuff aplicado (Slay the Spire)
    /// </summary>
    ARTIFACT,
    
    /// <summary>
    /// Reduz todo dano recebido para 1 (Slay the Spire)
    /// </summary>
    INTANGIBLE,
    
    /// <summary>
    /// Previne a próxima vez que HP chegaria a 0 (Slay the Spire)
    /// </summary>
    BUFFER,
    
    /// <summary>
    /// Block não expira no fim do turno (Slay the Spire)
    /// </summary>
    BARRICADE,
    
    /// <summary>
    /// Compra carta ao receber status (Slay the Spire)
    /// </summary>
    EVOLVE,
    
    // ===== CUSTOMIZÁVEIS =====
    
    /// <summary>
    /// Status effect customizado (definido via JSON)
    /// </summary>
    CUSTOM
}
