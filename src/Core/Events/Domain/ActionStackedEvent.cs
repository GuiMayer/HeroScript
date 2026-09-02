using Core.Combat.TurnPhase;
using Core.Events;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma ação é adicionada à pilha de resolução.
/// </summary>
public record ActionStackedEvent : GameEvent
{
    /// <summary>
    /// ID do combate
    /// </summary>
    public Guid CombatId { get; init; }
    
    /// <summary>
    /// Ação que foi adicionada à pilha
    /// </summary>
    public PendingAction Action { get; init; } = null!;
    
    /// <summary>
    /// Tamanho atual da pilha após adicionar a ação
    /// </summary>
    public int StackSize { get; init; }
    
    /// <summary>
    /// Fase atual
    /// </summary>
    public string CurrentPhaseId { get; init; } = string.Empty;
}
