using System.Collections.Immutable;
using Core.Events;

namespace Core.Damage.Events;

/// <summary>
/// Evento emitido quando o cálculo de dano é finalizado
/// </summary>
public sealed record DamageCalculatedEvent : GameEvent
{
    private ImmutableHashSet<string> _tags =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    public DamageCalculatedEvent()
    {
        EventType = nameof(DamageCalculatedEvent);
        Category = EventCategory.COMBAT;
        Severity = EventSeverity.DEBUG;
    }
    
    /// <summary>
    /// ID da ação que causou o dano
    /// </summary>
    public string ActionId { get; init; } = string.Empty;
    
    /// <summary>
    /// ID do atacante
    /// </summary>
    public string AttackerId { get; init; } = string.Empty;
    
    /// <summary>
    /// ID do alvo
    /// </summary>
    public string TargetId { get; init; } = string.Empty;
    
    /// <summary>
    /// Dano base (antes do pipeline)
    /// </summary>
    public float BaseDamage { get; init; }
    
    /// <summary>
    /// Dano final (após pipeline completo)
    /// </summary>
    public float FinalDamage { get; init; }
    
    /// <summary>
    /// Tier de crítico alcançado (0 = normal, 1+ = crítico)
    /// </summary>
    public int CritTier { get; init; }
    
    /// <summary>
    /// Tags da ação (physical, spell, fire, etc.)
    /// </summary>
    public IReadOnlySet<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableHashSet(StringComparer.Ordinal)
            ?? ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    }
    
    /// <summary>
    /// Metadata adicional do cálculo
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
