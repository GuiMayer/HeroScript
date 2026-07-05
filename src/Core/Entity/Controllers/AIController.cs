using Core.Combat.Models;
using Core.Common;
using Core.Entity.Components;
using Core.Logging;

namespace Core.Entity.Controllers;

/// <summary>
/// Tipo de comportamento de IA
/// </summary>
public enum AIBehaviorType
{
    /// <summary>
    /// Sempre ataca o alvo mais fraco
    /// </summary>
    AGGRESSIVE,
    
    /// <summary>
    /// Foge quando HP baixo, ataca quando seguro
    /// </summary>
    DEFENSIVE,
    
    /// <summary>
    /// Mix de ataque e defesa baseado em situação
    /// </summary>
    BALANCED
}

/// <summary>
/// Controller de IA básico para inimigos.
/// Usa behavior tree simples para tomar decisões.
/// </summary>
public class AIController : IEntityController
{
    public string ControllerId { get; }
    public EntityControllerType Type => EntityControllerType.AI_BEHAVIOR_TREE;
    
    private readonly AIBehaviorType _behaviorType;
    private readonly ILogger _logger;
    
    // Thresholds configuráveis
    private readonly float _lowHealthThreshold;
    private readonly float _fleeHealthThreshold;
    
    public AIController(
        AIBehaviorType behaviorType = AIBehaviorType.BALANCED,
        ILogger? logger = null,
        string controllerId = "ai_controller",
        float lowHealthThreshold = 0.5f,
        float fleeHealthThreshold = 0.3f)
    {
        ControllerId = controllerId;
        _behaviorType = behaviorType;
        _logger = logger ?? NullLogger.Instance;
        _lowHealthThreshold = lowHealthThreshold;
        _fleeHealthThreshold = fleeHealthThreshold;
    }
    
    public Task<Result<EntityAction>> DecideAction(
        Entity controlledEntity,
        CombatState combatState)
    {
        try
        {
            _logger.LogDebug($"AI {ControllerId} deciding action for {controlledEntity.EntityId}");
            
            // Obter componente de recursos
            var resourceComp = controlledEntity.GetComponent<ResourceComponent>();
            if (resourceComp == null)
            {
                return Task.FromResult(
                    Result<EntityAction>.Failure("Entity has no ResourceComponent"));
            }
            
            // Calcular HP percentage
            var health = resourceComp.GetResource("health");
            if (health == null)
            {
                return Task.FromResult(
                    Result<EntityAction>.Failure("Entity has no health resource"));
            }
            
            float hpPercent = health.Current / health.Maximum;
            
            // Decidir ação baseada no behavior type
            var action = _behaviorType switch
            {
                AIBehaviorType.AGGRESSIVE => DecideAggressive(controlledEntity, combatState, hpPercent),
                AIBehaviorType.DEFENSIVE => DecideDefensive(controlledEntity, combatState, hpPercent),
                AIBehaviorType.BALANCED => DecideBalanced(controlledEntity, combatState, hpPercent),
                _ => new EntityAction { ActionType = ActionType.PASS }
            };
            
            _logger.LogDebug($"AI decided: {action.ActionType} (HP: {hpPercent:P0})");
            
            return Task.FromResult(Result<EntityAction>.Success(action));
        }
        catch (Exception ex)
        {
            _logger.LogError($"AI decision failed: {ex.Message}", ex);
            return Task.FromResult(
                Result<EntityAction>.Failure($"AI decision error: {ex.Message}"));
        }
    }
    
    private EntityAction DecideAggressive(Entity entity, CombatState state, float hpPercent)
    {
        // Sempre ataca o alvo mais fraco
        var target = FindWeakestTarget(state);
        
        if (target == null)
        {
            return new EntityAction { ActionType = ActionType.PASS };
        }
        
        return new EntityAction
        {
            ActionType = ActionType.BASIC_ATTACK,
            TargetId = target.EntityId
        };
    }
    
    private EntityAction DecideDefensive(Entity entity, CombatState state, float hpPercent)
    {
        // Foge se HP baixo
        if (hpPercent < _fleeHealthThreshold)
        {
            _logger.LogDebug($"HP too low ({hpPercent:P0}), passing turn");
            return new EntityAction { ActionType = ActionType.PASS };
        }
        
        // Ataca se HP seguro
        var target = FindWeakestTarget(state);
        if (target == null)
        {
            return new EntityAction { ActionType = ActionType.PASS };
        }
        
        return new EntityAction
        {
            ActionType = ActionType.BASIC_ATTACK,
            TargetId = target.EntityId
        };
    }
    
    private EntityAction DecideBalanced(Entity entity, CombatState state, float hpPercent)
    {
        // Comportamento misto baseado em HP
        if (hpPercent < _lowHealthThreshold)
        {
            // HP baixo: comportamento defensivo
            return DecideDefensive(entity, state, hpPercent);
        }
        else
        {
            // HP alto: comportamento agressivo
            return DecideAggressive(entity, state, hpPercent);
        }
    }
    
    private CombatEntity? FindWeakestTarget(CombatState state)
    {
        // Encontra o alvo com menor HP
        // Prioriza o herói se estiver vivo
        if (state.Hero.IsAlive)
        {
            return state.Hero;
        }
        
        // Se herói morto, procura outros alvos (companions no futuro)
        return null;
    }
    
    public void OnTurnStart(Entity entity, CombatState state)
    {
        _logger.LogDebug($"AI {ControllerId} turn start for {entity.EntityId}");
    }
    
    public void OnTurnEnd(Entity entity, CombatState state)
    {
        _logger.LogDebug($"AI {ControllerId} turn end for {entity.EntityId}");
    }
    
    public void OnDamageTaken(Entity entity, float damage)
    {
        _logger.LogDebug($"AI {ControllerId} took {damage} damage");
    }
    
    public void OnDamageDealt(Entity entity, float damage)
    {
        _logger.LogDebug($"AI {ControllerId} dealt {damage} damage");
    }
}
