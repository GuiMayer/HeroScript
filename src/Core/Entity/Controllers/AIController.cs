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
    /// Foge quando o recurso de decisão está baixo, ataca quando seguro
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
    private readonly string _decisionResourceId;
    private readonly float _lowResourceThreshold;
    private readonly float _fleeResourceThreshold;
    
    public AIController(
        string decisionResourceId,
        AIBehaviorType behaviorType = AIBehaviorType.BALANCED,
        ILogger? logger = null,
        string controllerId = "ai_controller",
        float lowResourceThreshold = 0.5f,
        float fleeResourceThreshold = 0.3f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionResourceId);
        ValidateThreshold(lowResourceThreshold, nameof(lowResourceThreshold));
        ValidateThreshold(fleeResourceThreshold, nameof(fleeResourceThreshold));
        ControllerId = controllerId;
        _decisionResourceId = decisionResourceId;
        _behaviorType = behaviorType;
        _logger = logger ?? NullLogger.Instance;
        _lowResourceThreshold = lowResourceThreshold;
        _fleeResourceThreshold = fleeResourceThreshold;
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
            
            var decisionResource = resourceComp.GetResource(_decisionResourceId);
            if (decisionResource == null)
            {
                return Task.FromResult(
                    Result<EntityAction>.Failure(
                        $"Entity has no configured AI decision resource: {_decisionResourceId}"));
            }

            var resourcePercent = decisionResource.GetPercentage() / 100f;
            
            // Decidir ação baseada no behavior type
            var action = _behaviorType switch
            {
                AIBehaviorType.AGGRESSIVE => DecideAggressive(controlledEntity, combatState, resourcePercent),
                AIBehaviorType.DEFENSIVE => DecideDefensive(controlledEntity, combatState, resourcePercent),
                AIBehaviorType.BALANCED => DecideBalanced(controlledEntity, combatState, resourcePercent),
                _ => new EntityAction { ActionType = ActionType.PASS }
            };
            
            _logger.LogDebug(
                $"AI decided: {action.ActionType} ({_decisionResourceId}: {resourcePercent:P0})");
            
            return Task.FromResult(Result<EntityAction>.Success(action));
        }
        catch (Exception ex)
        {
            _logger.LogError($"AI decision failed: {ex.Message}", ex);
            return Task.FromResult(
                Result<EntityAction>.Failure($"AI decision error: {ex.Message}"));
        }
    }
    
    private EntityAction DecideAggressive(Entity entity, CombatState state, float resourcePercent)
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
    
    private EntityAction DecideDefensive(Entity entity, CombatState state, float resourcePercent)
    {
        if (resourcePercent < _fleeResourceThreshold)
        {
            _logger.LogDebug(
                $"{_decisionResourceId} too low ({resourcePercent:P0}), passing turn");
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
    
    private EntityAction DecideBalanced(Entity entity, CombatState state, float resourcePercent)
    {
        if (resourcePercent < _lowResourceThreshold)
        {
            // HP baixo: comportamento defensivo
            return DecideDefensive(entity, state, resourcePercent);
        }
        else
        {
            // HP alto: comportamento agressivo
            return DecideAggressive(entity, state, resourcePercent);
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

    private static void ValidateThreshold(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(parameterName, "AI threshold must be between 0 and 1");
    }
}
