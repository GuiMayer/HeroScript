namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando um efeito é executado.
/// </summary>
public record EffectExecutedEvent : GameEvent
{
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
    public Dictionary<string, object> Metadata { get; init; } = new();
}
