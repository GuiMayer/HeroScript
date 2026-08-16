using System.Collections.Immutable;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando um efeito é executado.
/// </summary>
public record EffectExecutedEvent : GameEvent
{
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>
    /// ID da instância do efeito executado
    /// </summary>
    public string EffectInstanceId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo do efeito
    /// </summary>
    public Effects.EffectType EffectType { get; init; }
    
    /// <summary>
    /// ID da entidade que originou o efeito
    /// </summary>
    public string SourceEntityId { get; init; } = string.Empty;
    
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    public string TargetEntityId { get; init; } = string.Empty;
    
    /// <summary>
    /// Valor aplicado (se aplicável)
    /// </summary>
    public float? ValueApplied { get; init; }
    
    /// <summary>
    /// Se o efeito foi executado com sucesso
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Metadata adicional sobre a execução
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
