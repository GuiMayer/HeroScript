using Core.Events;

namespace Core.Events.Domain;

public record PriorityPassedEvent : GameEvent
{
    /// <summary>
    /// ID do combate
    /// </summary>
    public Guid CombatId { get; init; }
    
    /// <summary>
    /// ID do jogador que passou prioridade
    /// </summary>
    public string PlayerId { get; init; } = "";
    
    /// <summary>
    /// Fase atual
    /// </summary>
    public string CurrentPhaseId { get; init; } = string.Empty;
    
    /// <summary>
    /// Se true, todos os jogadores passaram prioridade
    /// </summary>
    public bool AllPlayersPassed { get; init; }
}
