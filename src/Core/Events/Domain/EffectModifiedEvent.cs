namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando modificadores são aplicados a um efeito.
/// </summary>
public record EffectModifiedEvent : GameEvent
{
    /// <summary>
    /// ID da instância do efeito
    /// </summary>
    public string EffectInstanceId { get; init; } = string.Empty;
    
    /// <summary>
    /// IDs dos modificadores aplicados
    /// </summary>
    public List<string> ModifierIds { get; init; } = new();
    
    /// <summary>
    /// Definição original do efeito
    /// </summary>
    public Effects.EffectDefinition OriginalDefinition { get; init; } = null!;
    
    /// <summary>
    /// Definição modificada do efeito
    /// </summary>
    public Effects.EffectDefinition ModifiedDefinition { get; init; } = null!;
}
