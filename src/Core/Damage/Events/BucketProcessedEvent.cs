using System.Collections.Immutable;
using Core.Events;

namespace Core.Damage.Events;

/// <summary>
/// Evento emitido quando um bucket do pipeline é processado
/// </summary>
public sealed record BucketProcessedEvent : GameEvent
{
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    public BucketProcessedEvent()
    {
        EventType = nameof(BucketProcessedEvent);
        Category = EventCategory.PIPELINE;
        Severity = EventSeverity.DEBUG;
    }
    
    /// <summary>
    /// ID do bucket processado
    /// </summary>
    public string BucketId { get; init; } = string.Empty;
    
    /// <summary>
    /// Dano antes do bucket
    /// </summary>
    public float DamageBefore { get; init; }
    
    /// <summary>
    /// Dano depois do bucket
    /// </summary>
    public float DamageAfter { get; init; }
    
    /// <summary>
    /// Delta de dano (DamageAfter - DamageBefore)
    /// </summary>
    public float DamageDelta => DamageAfter - DamageBefore;
    
    /// <summary>
    /// Metadata do contexto no momento do processamento
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
