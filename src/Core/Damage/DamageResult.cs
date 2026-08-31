using System.Collections.Immutable;

namespace Core.Damage;

/// <summary>
/// Resultado do cálculo de dano
/// </summary>
public record DamageResult
{
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

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
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
