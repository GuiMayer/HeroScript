using Core.Events;

namespace Core.Damage.Events;

/// <summary>
/// Evento emitido quando a configuração do pipeline é recarregada
/// </summary>
public class PipelineReloadedEvent : IEvent
{
    /// <summary>
    /// Número de buckets na nova configuração
    /// </summary>
    public int BucketCount { get; init; }
    
    /// <summary>
    /// IDs dos buckets na ordem de execução
    /// </summary>
    public List<string> BucketIds { get; init; } = new();
    
    /// <summary>
    /// Motivo do reload (manual, file change, error recovery, etc.)
    /// </summary>
    public string Reason { get; init; } = string.Empty;
    
    /// <summary>
    /// Se o reload foi bem-sucedido
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Mensagem de erro (se houver)
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Timestamp do evento
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
