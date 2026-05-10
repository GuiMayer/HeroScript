using Core.Combat;
using Core.Common;

namespace Core.Entity.Controllers;

/// <summary>
/// Controller para companions controlados por sistema Gambit.
/// Stub - será implementado completamente na Fase 2 quando Gambit System estiver pronto.
/// </summary>
public class GambitController : IEntityController
{
    public string ControllerId { get; }
    public EntityControllerType Type => EntityControllerType.GAMBIT;
    
    // Placeholder para gambits - será implementado na Fase 2
    private readonly List<object> _gambits = new();
    
    public GambitController(string controllerId = "gambit_controller")
    {
        ControllerId = controllerId;
    }
    
    /// <summary>
    /// Avalia gambits e decide ação.
    /// Stub - sempre retorna PASS até Gambit System estar implementado.
    /// </summary>
    public Task<Result<EntityAction>> DecideAction(
        Entity controlledEntity,
        CombatState combatState)
    {
        // TODO: Implementar quando Gambit System (Fase 2) estiver pronto
        // Por enquanto, sempre passa o turno
        var action = new EntityAction
        {
            ActionType = ActionType.PASS
        };
        
        return Task.FromResult(Result<EntityAction>.Success(action));
    }
    
    public void OnTurnStart(Entity entity, CombatState state)
    {
        // TODO: Processar gambits de início de turno
    }
    
    public void OnTurnEnd(Entity entity, CombatState state)
    {
        // TODO: Processar gambits de fim de turno
    }
    
    public void OnDamageTaken(Entity entity, float damage)
    {
        // TODO: Processar gambits de reação a dano
    }
    
    public void OnDamageDealt(Entity entity, float damage)
    {
        // TODO: Processar gambits de reação a dano causado
    }
}
