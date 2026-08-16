using Core.Combat.TurnPhase;
using Core.Events;

namespace Core.Events.Domain;

public record PhaseEndedEvent : GameEvent
{
    /// <summary>
    /// ID do combate
    /// </summary>
    public Guid CombatId { get; init; }
    
    /// <summary>
    /// Fase que foi finalizada
    /// </summary>
    public TurnPhase Phase { get; init; }
    
    /// <summary>
    /// Próxima fase (se houver)
    /// </summary>
    public TurnPhase? NextPhase { get; init; }
    
    /// <summary>
    /// Número do turno atual
    /// </summary>
    public new int Turn { get; init; }
    
    /// <summary>
    /// Duração da fase em milissegundos
    /// </summary>
    public long DurationMs { get; init; }
}
