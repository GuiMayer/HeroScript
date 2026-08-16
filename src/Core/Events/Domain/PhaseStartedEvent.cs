using Core.Combat.TurnPhase;
using Core.Events;

namespace Core.Events.Domain;

public record PhaseStartedEvent : GameEvent
{
    /// <summary>
    /// ID do combate
    /// </summary>
    public Guid CombatId { get; init; }
    
    /// <summary>
    /// Fase que foi iniciada
    /// </summary>
    public TurnPhase Phase { get; init; }
    
    /// <summary>
    /// ID do jogador que tem prioridade nesta fase
    /// </summary>
    public string ActivePlayerId { get; init; } = "";
    
    /// <summary>
    /// Número do turno atual
    /// </summary>
    public new int Turn { get; init; }
}
