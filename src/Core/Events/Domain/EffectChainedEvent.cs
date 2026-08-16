using System.Collections.Immutable;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando efeitos encadeados são disparados.
/// </summary>
public record EffectChainedEvent : GameEvent
{
    private ImmutableList<string> _chainedEffectIds = [];

    /// <summary>
    /// ID do efeito pai que disparou os efeitos encadeados
    /// </summary>
    public string ParentEffectId { get; init; } = string.Empty;
    
    /// <summary>
    /// IDs dos efeitos encadeados gerados
    /// </summary>
    public IReadOnlyList<string> ChainedEffectIds
    {
        get => _chainedEffectIds;
        init => _chainedEffectIds = value?.ToImmutableList() ?? [];
    }
}
