namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando efeitos encadeados são disparados.
/// </summary>
public record EffectChainedEvent : GameEvent
{
    /// <summary>
    /// ID do efeito pai que disparou os efeitos encadeados
    /// </summary>
    public string ParentEffectId { get; init; } = string.Empty;
    
    /// <summary>
    /// IDs dos efeitos encadeados gerados
    /// </summary>
    public List<string> ChainedEffectIds { get; init; } = new();
}
