using System.Collections.Immutable;

namespace Core.Events;

/// <summary>
/// Evento imutável do jogo com estrutura completa para Event Sourcing.
/// Baseado na estrutura LogEntry da arquitetura.
/// </summary>
public record GameEvent : IEvent
{
    private ImmutableDictionary<string, object> _payload =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    private ImmutableDictionary<string, object>? _stateBefore;
    private ImmutableDictionary<string, object>? _stateAfter;
    private ImmutableDictionary<string, object>? _delta;

    // Metadados
    public Guid EventId { get; init; } = Guid.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UnixEpoch;
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
    public IReadOnlyDictionary<string, object> Payload
    {
        get => _payload;
        init => _payload = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
    
    // Estado (para Event Sourcing e replay)
    public IReadOnlyDictionary<string, object>? StateBefore
    {
        get => _stateBefore;
        init => _stateBefore = value?.ToImmutableDictionary(StringComparer.Ordinal);
    }
    public IReadOnlyDictionary<string, object>? StateAfter
    {
        get => _stateAfter;
        init => _stateAfter = value?.ToImmutableDictionary(StringComparer.Ordinal);
    }
    public IReadOnlyDictionary<string, object>? Delta
    {
        get => _delta;
        init => _delta = value?.ToImmutableDictionary(StringComparer.Ordinal);
    }
}
