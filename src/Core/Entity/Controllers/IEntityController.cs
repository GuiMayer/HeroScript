using Core.Combat.Models;
using Core.Common;

namespace Core.Entity.Controllers;

/// <summary>
/// Tipo de controller de entidade
/// </summary>
public enum EntityControllerType
{
    /// <summary>
    /// Controlado por input do jogador
    /// </summary>
    PLAYER_INPUT,
    
    /// <summary>
    /// Controlado por sistema Gambit (companions)
    /// </summary>
    GAMBIT,
    
    /// <summary>
    /// Controlado por behavior tree (IA)
    /// </summary>
    AI_BEHAVIOR_TREE,
    
    /// <summary>
    /// Controlado por script customizado
    /// </summary>
    AI_SCRIPTED
}

/// <summary>
/// Ação decidida por um controller
/// </summary>
public record EntityAction
{
    public ActionType ActionType { get; init; }
    public string? PowerId { get; init; }
    public string? TargetId { get; init; }
    public int? CostOptionId { get; init; }
}

/// <summary>
/// Interface para controllers de entidade.
/// Controllers decidem quais ações uma entidade deve tomar.
/// </summary>
public interface IEntityController
{
    /// <summary>
    /// ID único do controller
    /// </summary>
    string ControllerId { get; }
    
    /// <summary>
    /// Tipo do controller
    /// </summary>
    EntityControllerType Type { get; }
    
    /// <summary>
    /// Decide qual ação a entidade deve tomar
    /// </summary>
    Task<Result<EntityAction>> DecideAction(
        Entity controlledEntity,
        CombatState combatState);
    
    /// <summary>
    /// Chamado no início do turno da entidade
    /// </summary>
    void OnTurnStart(Entity entity, CombatState state);
    
    /// <summary>
    /// Chamado no fim do turno da entidade
    /// </summary>
    void OnTurnEnd(Entity entity, CombatState state);
    
    /// <summary>
    /// Chamado quando a entidade recebe dano
    /// </summary>
    void OnDamageTaken(Entity entity, float damage);
    
    /// <summary>
    /// Chamado quando a entidade causa dano
    /// </summary>
    void OnDamageDealt(Entity entity, float damage);
}
