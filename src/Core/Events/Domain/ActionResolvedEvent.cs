using Core.Combat.TurnPhase;
using Core.Events;

namespace Core.Events.Domain;

/// <summary>
/// Evento publicado quando uma ação é resolvida da pilha.
/// </summary>
public record ActionResolvedEvent : GameEvent
{
    /// <summary>
    /// ID do combate
    /// </summary>
    public Guid CombatId { get; init; }
    
    /// <summary>
    /// Ação que foi resolvida
    /// </summary>
    public PendingAction Action { get; init; } = null!;
    
    /// <summary>
    /// Tamanho atual da pilha após resolver a ação
    /// </summary>
    public int StackSize { get; init; }
    
    /// <summary>
    /// Fase atual
    /// </summary>
    public string CurrentPhaseId { get; init; } = string.Empty;
    
    /// <summary>
    /// Se true, a pilha está agora vazia
    /// </summary>
    public bool StackEmpty { get; init; }
}
