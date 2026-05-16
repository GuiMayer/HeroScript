namespace API.Models;

/// <summary>
/// Response do cálculo de dano
/// </summary>
public class CalculateDamageResponse
{
    public string ActionId { get; set; } = string.Empty;
    public string Mode { get; set; } = "simulation";

    /// <summary>
    /// Dano final calculado
    /// </summary>
    public float FinalDamage { get; set; }
    
    /// <summary>
    /// Tier de crítico alcançado (0 = normal, 1+ = crítico)
    /// </summary>
    public int CritTier { get; set; }
    
    /// <summary>
    /// Dano base (antes do pipeline)
    /// </summary>
    public float BaseDamage { get; set; }
    
    /// <summary>
    /// Breakdown do cálculo por bucket
    /// </summary>
    public List<BucketBreakdownDto> Breakdown { get; set; } = new();
    
    /// <summary>
    /// Metadata adicional do cálculo
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Breakdown de um bucket do pipeline
/// </summary>
public class BucketBreakdownDto
{
    /// <summary>
    /// ID do bucket
    /// </summary>
    public string BucketId { get; set; } = string.Empty;
    
    /// <summary>
    /// Dano antes do bucket
    /// </summary>
    public float DamageBefore { get; set; }
    
    /// <summary>
    /// Dano depois do bucket
    /// </summary>
    public float DamageAfter { get; set; }
    
    /// <summary>
    /// Delta de dano
    /// </summary>
    public float DamageDelta { get; set; }
}
