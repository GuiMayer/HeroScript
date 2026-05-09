using Core.Events;

namespace Core.Damage.Events;

/// <summary>
/// Evento emitido quando um bucket do pipeline é processado
/// </summary>
public class BucketProcessedEvent : IEvent
{
    /// <summary>
    /// Identificador único do evento
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// Tipo do evento
    /// </summary>
    public string EventType { get; init; } = nameof(BucketProcessedEvent);
    
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
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    /// <summary>
    /// Timestamp do evento
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
