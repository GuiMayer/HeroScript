using Core.Combat;
using Core.Common;

namespace Core.Entity.Controllers;

/// <summary>
/// Controller para entidades controladas pelo jogador.
/// Aguarda input do jogador - não toma decisões automaticamente.
/// </summary>
public class PlayerController : IEntityController
{
    public string ControllerId { get; }
    public EntityControllerType Type => EntityControllerType.PLAYER_INPUT;
    
    public PlayerController(string controllerId = "player_controller")
    {
        ControllerId = controllerId;
    }
    
    /// <summary>
    /// Player controller não decide ações automaticamente.
    /// Retorna erro indicando que aguarda input do jogador.
    /// </summary>
    public Task<Result<EntityAction>> DecideAction(
        Entity controlledEntity,
        CombatState combatState)
    {
        return Task.FromResult(
            Result<EntityAction>.Failure("Awaiting player input"));
    }
    
    public void OnTurnStart(Entity entity, CombatState state)
    {
        // Placeholder - pode ser usado para UI notifications
    }
    
    public void OnTurnEnd(Entity entity, CombatState state)
    {
        // Placeholder - pode ser usado para UI notifications
    }
    
    public void OnDamageTaken(Entity entity, float damage)
    {
        // Placeholder - pode ser usado para UI feedback
    }
    
    public void OnDamageDealt(Entity entity, float damage)
    {
        // Placeholder - pode ser usado para UI feedback
    }
}
