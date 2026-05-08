namespace Core.Events;

/// <summary>
/// Evento imutável do jogo com estrutura completa para Event Sourcing.
/// Baseado na estrutura LogEntry da arquitetura.
/// </summary>
public record GameEvent : IEvent
{
    // Metadados
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string EventType { get; init; } = string.Empty;
    public int Turn { get; init; }
    public int Sequence { get; init; }
    
    // Classificação
    public EventCategory Category { get; init; }
    public EventSeverity Severity { get; init; }
    
    // Conteúdo (notação de combate: subject-verb-target)
    public string Subject { get; init; } = string.Empty;
    public string Verb { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public Dictionary<string, object> Payload { get; init; } = new();
    
    // Estado (para Event Sourcing e replay)
    public Dictionary<string, object>? StateBefore { get; init; }
    public Dictionary<string, object>? StateAfter { get; init; }
    public Dictionary<string, object>? Delta { get; init; }
}
