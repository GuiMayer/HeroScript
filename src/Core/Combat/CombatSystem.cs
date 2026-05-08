using Core.Common;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Resources;
using System.Collections.Concurrent;

namespace Core.Combat;

/// <summary>
/// Sistema de combate com gerenciamento de estado imutável.
/// Thread-safe usando ConcurrentDictionary.
/// Usa sistema de recursos genérico configurável via JSON.
/// </summary>
public class CombatSystem : ICombatSystem
{
    private readonly ConcurrentDictionary<Guid, CombatState> _activeCombats = new();
    private readonly IEventBus? _eventBus;
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    
    // Constantes de gameplay (futuramente virão de config)
    private const int BASIC_ATTACK_DAMAGE = 10;
    private const int BASIC_ATTACK_ENERGY_GAIN = 1;
    private const int DEFAULT_POWER_COST = 3;
    private const int DEFAULT_POWER_DAMAGE = 30;
    
    public CombatSystem(ILogger logger, IResourceManager resourceManager, IEventBus? eventBus = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _eventBus = eventBus;
    }
    
    public Result<CombatState> StartCombat(string heroId, List<string> enemyIds, int initialEnergy = 3)
    {
        try
        {
            // Validações
            if (string.IsNullOrWhiteSpace(heroId))
                return Result<CombatState>.Failure("Hero ID cannot be empty");
            
            if (enemyIds == null || enemyIds.Count == 0)
                return Result<CombatState>.Failure("At least one enemy is required");
            
            if (initialEnergy < 0 || initialEnergy > 10)
                return Result<CombatState>.Failure("Initial energy must be between 0 and 10");
            
            // Criar recursos do herói
            var heroHealthPool = _resourceManager.CreatePool("health", 100);
            var heroEnergyPool = _resourceManager.CreatePool("energy", initialEnergy);
            
            var heroResources = new Dictionary<string, ResourcePool>
            {
                ["health"] = heroHealthPool,
                ["energy"] = heroEnergyPool
            };
            
            var heroResourceState = new EntityResourceState
            {
                EntityId = heroId,
                Resources = heroResources
            };
            
            // Criar herói
            var hero = new CombatEntity
            {
                EntityId = heroId,
                Name = "Hero",
                IsHero = true,
                ResourceState = heroResourceState
            };
            
            // Criar inimigos
            var enemies = enemyIds.Select((id, index) =>
            {
                var enemyHealthPool = _resourceManager.CreatePool("health", 50);
                var enemyResources = new Dictionary<string, ResourcePool>
                {
                    ["health"] = enemyHealthPool
                };
                
                var enemyResourceState = new EntityResourceState
                {
                    EntityId = id,
                    Resources = enemyResources
                };
                
                return new CombatEntity
                {
                    EntityId = id,
                    Name = $"Enemy-{index + 1}",
                    IsHero = false,
                    ResourceState = enemyResourceState
                };
            }).ToList();
            
            // Criar estado inicial
            var combatState = new CombatState
            {
                Hero = hero,
                Enemies = enemies,
                CurrentTurn = 1,
                Status = CombatStatus.ACTIVE
            };
            
            // Adicionar ao dicionário
            if (!_activeCombats.TryAdd(combatState.CombatId, combatState))
                return Result<CombatState>.Failure("Failed to create combat (ID collision)");
            
            // Publicar evento
            _eventBus?.Publish(new CombatStartedEvent
            {
                CombatId = combatState.CombatId,
                HeroId = heroId,
                EnemyIds = enemyIds,
                InitialEnergy = initialEnergy,
                Target = combatState.CombatId.ToString()
            });
            
            _logger.LogInformation($"Combat started: {combatState.CombatId}");
            return Result<CombatState>.Success(combatState);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error starting combat: {ex.Message}");
            return Result<CombatState>.Failure($"Failed to start combat: {ex.Message}");
        }
    }
    
    public Result<CombatState> ExecuteAction(Guid combatId, ActionType actionType, string? powerId = null, string? targetId = null, string? costOptionId = null)
    {
        try
        {
            // Obter estado atual
            if (!_activeCombats.TryGetValue(combatId, out var currentState))
                return Result<CombatState>.Failure($"Combat {combatId} not found");
            
            if (!currentState.IsActive)
                return Result<CombatState>.Failure($"Combat {combatId} is not active (status: {currentState.Status})");
            
            // Validar ação
            var validationResult = ValidateAction(currentState, actionType, powerId, targetId, costOptionId);
            if (validationResult.IsFailure)
                return Result<CombatState>.Failure(validationResult.Error);
            
            // Executar ação e criar novo estado
            var newState = actionType switch
            {
                ActionType.BASIC_ATTACK => ExecuteBasicAttack(currentState, targetId!),
                ActionType.POWER => ExecutePower(currentState, powerId!, targetId!),
                ActionType.PASS => ExecutePass(currentState),
                ActionType.END_TURN => ExecuteEndTurn(currentState),
                _ => throw new InvalidOperationException($"Unknown action type: {actionType}")
            };
            
            // Verificar condições de vitória/derrota
            newState = CheckCombatEnd(newState);
            
            // Atualizar estado
            _activeCombats[combatId] = newState;
            
            // Publicar evento da ação
            var lastAction = newState.ActionHistory.Last();
            _eventBus?.Publish(new ActionExecutedEvent
            {
                CombatId = combatId,
                ActionId = lastAction.ActionId,
                ActorId = lastAction.ActorId,
                ActionTypeName = lastAction.ActionType.ToString(),
                PowerId = lastAction.PowerId,
                TargetId = lastAction.TargetId,
                DamageDealt = lastAction.DamageDealt,
                EnergyChange = lastAction.EnergyChange,
                Turn = newState.CurrentTurn,
                Subject = lastAction.ActorId,
                Target = lastAction.TargetId ?? "none"
            });
            
            return Result<CombatState>.Success(newState);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error executing action: {ex.Message}");
            return Result<CombatState>.Failure($"Failed to execute action: {ex.Message}");
        }
    }
    
    private Result<bool> ValidateAction(CombatState state, ActionType actionType, string? powerId, string? targetId, string? costOptionId)
    {
        switch (actionType)
        {
            case ActionType.BASIC_ATTACK:
                if (string.IsNullOrWhiteSpace(targetId))
                    return Result<bool>.Failure("Target is required for basic attack");
                if (state.GetEntity(targetId) == null)
                    return Result<bool>.Failure($"Target {targetId} not found");
                if (!state.GetEntity(targetId)!.IsAlive)
                    return Result<bool>.Failure($"Target {targetId} is already dead");
                break;
                
            case ActionType.POWER:
                if (string.IsNullOrWhiteSpace(powerId))
                    return Result<bool>.Failure("Power ID is required");
                if (string.IsNullOrWhiteSpace(targetId))
                    return Result<bool>.Failure("Target is required for power");
                
                var energyPool = state.GetHeroResource("energy");
                if (energyPool == null || !energyPool.CanAfford(DEFAULT_POWER_COST))
                    return Result<bool>.Failure($"Insufficient energy: has {energyPool?.Current ?? 0}, needs {DEFAULT_POWER_COST}");
                
                if (state.GetEntity(targetId) == null)
                    return Result<bool>.Failure($"Target {targetId} not found");
                if (!state.GetEntity(targetId)!.IsAlive)
                    return Result<bool>.Failure($"Target {targetId} is already dead");
                
                // TODO: When ActionManager is integrated, validate costOptionId here
                // Example:
                // var actionDef = _actionManager.GetDefinition(powerId);
                // if (actionDef.Costs.AlternativeCosts.Count > 0)
                // {
                //     if (string.IsNullOrWhiteSpace(costOptionId))
                //         return Result<bool>.Failure("Cost option must be specified for this action");
                //     
                //     if (!actionDef.Costs.CanAffordOption(costOptionId, state.Hero.ResourceState.Resources))
                //         return Result<bool>.Failure($"Cannot afford cost option: {costOptionId}");
                // }
                break;
                
            case ActionType.PASS:
            case ActionType.END_TURN:
                // Sempre válido
                break;
                
            default:
                return Result<bool>.Failure($"Unknown action type: {actionType}");
        }
        
        return Result<bool>.Success(true);
    }
    
    private CombatState ExecuteBasicAttack(CombatState state, string targetId)
    {
        var target = state.GetEntity(targetId)!;
        var newTarget = target.TakeDamage(BASIC_ATTACK_DAMAGE);
        
        // Ganhar energia
        var energyPool = state.GetHeroResource("energy")!;
        var newEnergyPool = energyPool.Gain(BASIC_ATTACK_ENERGY_GAIN);
        var newHero = state.Hero.UpdateResource("energy", newEnergyPool);
        
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = state.Hero.EntityId,
            ActionType = ActionType.BASIC_ATTACK,
            TargetId = targetId,
            DamageDealt = BASIC_ATTACK_DAMAGE,
            EnergyChange = BASIC_ATTACK_ENERGY_GAIN
        };
        
        var newEnemies = state.Enemies.Select(e => e.EntityId == targetId ? newTarget : e).ToList();
        var newHistory = state.ActionHistory.Append(action).ToList();
        
        // Publicar evento de energia
        _eventBus?.Publish(new EnergyChangedEvent
        {
            CombatId = state.CombatId,
            OldEnergy = (int)energyPool.Current,
            NewEnergy = (int)newEnergyPool.Current,
            Delta = BASIC_ATTACK_ENERGY_GAIN,
            Reason = "Basic attack",
            Turn = state.CurrentTurn,
            Target = state.Hero.EntityId
        });
        
        return state with
        {
            Hero = newHero,
            Enemies = newEnemies,
            ActionHistory = newHistory
        };
    }
    
    private CombatState ExecutePower(CombatState state, string powerId, string targetId)
    {
        var target = state.GetEntity(targetId)!;
        var newTarget = target.TakeDamage(DEFAULT_POWER_DAMAGE);
        
        // Gastar energia
        var energyPool = state.GetHeroResource("energy")!;
        var newEnergyPool = energyPool.Spend(DEFAULT_POWER_COST);
        var newHero = state.Hero.UpdateResource("energy", newEnergyPool);
        
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = state.Hero.EntityId,
            ActionType = ActionType.POWER,
            PowerId = powerId,
            TargetId = targetId,
            DamageDealt = DEFAULT_POWER_DAMAGE,
            EnergyChange = -DEFAULT_POWER_COST
        };
        
        var newEnemies = state.Enemies.Select(e => e.EntityId == targetId ? newTarget : e).ToList();
        var newHistory = state.ActionHistory.Append(action).ToList();
        
        // Publicar evento de energia
        _eventBus?.Publish(new EnergyChangedEvent
        {
            CombatId = state.CombatId,
            OldEnergy = (int)energyPool.Current,
            NewEnergy = (int)newEnergyPool.Current,
            Delta = -DEFAULT_POWER_COST,
            Reason = $"Power: {powerId}",
            Turn = state.CurrentTurn,
            Target = state.Hero.EntityId
        });
        
        return state with
        {
            Hero = newHero,
            Enemies = newEnemies,
            ActionHistory = newHistory
        };
    }
    
    private CombatState ExecutePass(CombatState state)
    {
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = state.Hero.EntityId,
            ActionType = ActionType.PASS
        };
        
        var newHistory = state.ActionHistory.Append(action).ToList();
        
        return state with { ActionHistory = newHistory };
    }
    
    private CombatState ExecuteEndTurn(CombatState state)
    {
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = state.Hero.EntityId,
            ActionType = ActionType.END_TURN
        };
        
        var newHistory = state.ActionHistory.Append(action).ToList();
        
        return state with
        {
            ActionHistory = newHistory,
            CurrentTurn = state.CurrentTurn + 1
        };
    }
    
    /// <summary>
    /// Aplica custos de uma ação ao herói.
    /// Se costOptionId for fornecido, aplica custos da opção alternativa.
    /// Caso contrário, aplica custos normais.
    /// </summary>
    /// <remarks>
    /// TODO: This method is prepared for future integration with ActionManager.
    /// Currently, CombatSystem uses hardcoded costs (DEFAULT_POWER_COST).
    /// When ActionManager is integrated, replace hardcoded logic with this method.
    /// </remarks>
    private CombatEntity ApplyCosts(
        CombatEntity hero, 
        ActionCosts costs, 
        string? costOptionId = null)
    {
        var updates = new Dictionary<string, ResourcePool>();
        
        // Se houver opção de custo alternativo, usar ela
        if (!string.IsNullOrWhiteSpace(costOptionId) && costs.AlternativeCosts.Count > 0)
        {
            var option = costs.GetOption(costOptionId);
            if (option == null)
                throw new InvalidOperationException($"Cost option not found: {costOptionId}");
            
            // Aplicar custos da opção
            foreach (var cost in option.Costs)
            {
                var pool = hero.GetResource(cost.ResourceId);
                if (pool == null)
                    throw new InvalidOperationException($"Resource not found: {cost.ResourceId}");
                
                var newPool = pool.Spend(cost.Amount);
                updates[cost.ResourceId] = newPool;
            }
        }
        else
        {
            // Aplicar custos normais
            foreach (var cost in costs.Costs)
            {
                var pool = hero.GetResource(cost.ResourceId);
                if (pool == null)
                    throw new InvalidOperationException($"Resource not found: {cost.ResourceId}");
                
                var newPool = pool.Spend(cost.Amount);
                updates[cost.ResourceId] = newPool;
            }
        }
        
        return hero.UpdateResources(updates);
    }
    
    private CombatState CheckCombatEnd(CombatState state)
    {
        if (state.AllEnemiesDead)
            return state with { Status = CombatStatus.VICTORY };
        
        if (state.HeroIsDead)
            return state with { Status = CombatStatus.DEFEAT };
        
        return state;
    }
    
    public Result<CombatState> GetCombatState(Guid combatId)
    {
        if (!_activeCombats.TryGetValue(combatId, out var state))
            return Result<CombatState>.Failure($"Combat {combatId} not found");
        
        return Result<CombatState>.Success(state);
    }
    
    public Result<CombatResult> EndCombat(Guid combatId)
    {
        if (!_activeCombats.TryRemove(combatId, out var state))
            return Result<CombatResult>.Failure($"Combat {combatId} not found");
        
        var result = new CombatResult
        {
            CombatId = combatId,
            Status = state.Status,
            TotalTurns = state.CurrentTurn,
            TotalActions = state.ActionHistory.Count,
            DamageDealt = state.ActionHistory.Sum(a => a.DamageDealt ?? 0),
            DamageTaken = state.Hero.MaxHp - state.Hero.CurrentHp,
            Duration = DateTime.UtcNow - state.StartedAt
        };
        
        _eventBus?.Publish(new CombatEndedEvent
        {
            CombatId = combatId,
            StatusName = state.Status.ToString(),
            TotalTurns = result.TotalTurns,
            TotalActions = result.TotalActions,
            Duration = result.Duration,
            Target = state.Hero.EntityId
        });
        
        _logger.LogInformation($"Combat ended: {combatId} - {state.Status}");
        return Result<CombatResult>.Success(result);
    }
    
    public Result<IReadOnlyList<CombatAction>> GetActionHistory(Guid combatId)
    {
        if (!_activeCombats.TryGetValue(combatId, out var state))
            return Result<IReadOnlyList<CombatAction>>.Failure($"Combat {combatId} not found");
        
        return Result<IReadOnlyList<CombatAction>>.Success(state.ActionHistory);
    }
    
    public bool CombatExists(Guid combatId)
    {
        return _activeCombats.ContainsKey(combatId);
    }
    
    public void ClearInactiveCombats()
    {
        var inactiveCombats = _activeCombats
            .Where(kvp => !kvp.Value.IsActive)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var combatId in inactiveCombats)
        {
            _activeCombats.TryRemove(combatId, out _);
        }
        
        _logger.LogInformation($"Cleared {inactiveCombats.Count} inactive combats");
    }
}
