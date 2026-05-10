namespace Core.StatusEffects;

/// <summary>
/// Define quando um status effect é processado.
/// Determina o timing de execução do efeito.
/// </summary>
public enum StatusEffectTiming
{
    /// <summary>
    /// Processa no início do turno da entidade
    /// Ex: Regeneration cura no início do turno
    /// </summary>
    START_OF_TURN,
    
    /// <summary>
    /// Processa no fim do turno da entidade
    /// Ex: Burning causa dano no fim do turno
    /// </summary>
    END_OF_TURN,
    
    /// <summary>
    /// Processa quando a entidade causa dano
    /// Ex: Thorns reflete dano ao atacar
    /// </summary>
    ON_DAMAGE_DEALT,
    
    /// <summary>
    /// Processa quando a entidade recebe dano
    /// Ex: Thorns reflete dano ao ser atacado
    /// </summary>
    ON_DAMAGE_TAKEN,
    
    /// <summary>
    /// Processa quando outro status é aplicado
    /// Ex: Evolve (Slay the Spire) - compra carta ao receber status
    /// </summary>
    ON_STATUS_APPLIED,
    
    /// <summary>
    /// Processa quando outro status é removido
    /// Ex: Efeito que dispara ao remover debuff
    /// </summary>
    ON_STATUS_REMOVED,
    
    /// <summary>
    /// Não expira, permanece até ser removido manualmente
    /// Ex: Strength, Artifact
    /// </summary>
    PERMANENT
}
