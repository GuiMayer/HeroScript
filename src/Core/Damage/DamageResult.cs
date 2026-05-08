namespace Core.Damage;

/// <summary>
/// Resultado do cálculo de dano
/// </summary>
public record DamageResult
{
    /// <summary>
    /// Dano final calculado (após todos os buckets)
    /// </summary>
    public float FinalDamage { get; init; }
    
    /// <summary>
    /// Tier de crítico alcançado (0 = normal, 1+ = crítico)
    /// </summary>
    public int CritTier { get; init; }
    
    /// <summary>
    /// Metadata adicional do cálculo (ex: crit_tier, attacker_id, etc.)
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
