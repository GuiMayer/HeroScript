using Core.Events;

namespace Core.Damage.Events;

/// <summary>
/// Evento emitido quando o cálculo de dano é finalizado
/// </summary>
public class DamageCalculatedEvent : IEvent
{
    /// <summary>
    /// Identificador único do evento
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// Tipo do evento
    /// </summary>
    public string EventType { get; init; } = nameof(DamageCalculatedEvent);
    
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
    public HashSet<string> Tags { get; init; } = new();
    
    /// <summary>
    /// Metadata adicional do cálculo
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    /// <summary>
    /// Timestamp do evento
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
