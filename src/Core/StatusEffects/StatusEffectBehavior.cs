namespace Core.StatusEffects;

/// <summary>
/// Define o comportamento de um status effect.
/// Determina como o status effect é processado e aplicado.
/// </summary>
public enum StatusEffectBehavior
{
    /// <summary>
    /// Causa dano por turno (DoT - Damage over Time)
    /// Ex: Burning, Poison, Bleeding
    /// </summary>
    DAMAGE_OVER_TIME,
    
    /// <summary>
    /// Cura HP por turno (HoT - Heal over Time)
    /// Ex: Regeneration
    /// </summary>
    HEAL_OVER_TIME,
    
    /// <summary>
    /// Modifica atributos da entidade
    /// Ex: Strength (aumenta dano), Weakness (reduz dano)
    /// </summary>
    STAT_MODIFIER,
    
    /// <summary>
    /// Impede ações da entidade
    /// Ex: Stunned (não pode agir), Silenced (não pode usar poderes)
    /// </summary>
    CONTROL,
    
    /// <summary>
    /// Absorve dano antes de afetar HP
    /// Ex: Shield, Barrier
    /// </summary>
    SHIELD,
    
    /// <summary>
    /// Reage a eventos específicos
    /// Ex: Thorns (reflete dano ao receber ataque)
    /// </summary>
    REACTIVE,
    
    // ===== BEHAVIORS ESPECIAIS (Slay the Spire) =====
    
    /// <summary>
    /// Previne o próximo debuff aplicado
    /// Ex: Artifact (Slay the Spire)
    /// Consome 1 stack ao prevenir um debuff
    /// </summary>
    PREVENT_NEXT_DEBUFF,
    
    /// <summary>
    /// Limita dano recebido a um valor máximo
    /// Ex: Intangible (Slay the Spire) - cap damage to 1
    /// </summary>
    DAMAGE_CAP,
    
    /// <summary>
    /// Previne morte (HP chegando a 0)
    /// Ex: Buffer (Slay the Spire)
    /// Consome 1 stack ao prevenir morte
    /// </summary>
    DEATH_PREVENTION,
    
    /// <summary>
    /// Modifica regras do jogo
    /// Ex: Barricade (Slay the Spire) - Block não expira no fim do turno
    /// </summary>
    RULE_MODIFIER,
    
    /// <summary>
    /// Dispara efeito ao receber outro status
    /// Ex: Evolve (Slay the Spire) - Compra carta ao receber status
    /// </summary>
    TRIGGER_ON_STATUS
}
