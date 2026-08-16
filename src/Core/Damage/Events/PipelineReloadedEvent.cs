using System.Collections.Immutable;
using Core.Events;

namespace Core.Damage.Events;

/// <summary>
/// Evento emitido quando a configuração do pipeline é recarregada
/// </summary>
public sealed record PipelineReloadedEvent : GameEvent
{
    private ImmutableList<string> _bucketIds = [];

    public PipelineReloadedEvent()
    {
        EventType = nameof(PipelineReloadedEvent);
        Category = EventCategory.PIPELINE;
        Severity = EventSeverity.INFO;
    }
    
    /// <summary>
    /// Número de buckets na nova configuração
    /// </summary>
    public int BucketCount { get; init; }
    
    /// <summary>
    /// IDs dos buckets na ordem de execução
    /// </summary>
    public IReadOnlyList<string> BucketIds
    {
        get => _bucketIds;
        init => _bucketIds = value?.ToImmutableList() ?? [];
    }
    
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
    
}
