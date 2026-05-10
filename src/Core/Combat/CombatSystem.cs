using Core.Common;
using Core.Damage;
using Core.Effects;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Resources;
using Core.StatusEffects;
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
    private readonly IDamageCalculator? _damageCalculator;
    private readonly IStatusEffectManager? _statusEffectManager;
    
    // Constantes de gameplay (futuramente virão de config)
    private const int BASIC_ATTACK_DAMAGE = 10;
    private const int BASIC_ATTACK_ENERGY_GAIN = 1;
    private const int DEFAULT_POWER_COST = 3;
    private const int DEFAULT_POWER_DAMAGE = 30;
    
    public CombatSystem(
        ILogger logger, 
        IResourceManager resourceManager, 
        IEventBus? eventBus = null, 
        IDamageCalculator? damageCalculator = null,
        IStatusEffectManager? statusEffectManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _eventBus = eventBus;
        _damageCalculator = damageCalculator;
        _statusEffectManager = statusEffectManager;
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
        // Verificar se o herói está sob controle (STUN, FREEZE, etc.)
        if (actionType != ActionType.PASS && actionType != ActionType.END_TURN)
        {
            if (_statusEffectManager != null && Guid.TryParse(state.Hero.EntityId, out var heroGuid))
            {
                var activeStatusResult = _statusEffectManager.GetActiveStatus(heroGuid);
                if (activeStatusResult.IsSuccess)
                {
                    var hasControlEffect = activeStatusResult.Value.Any(s => 
                        s.Definition.Behavior == StatusEffectBehavior.CONTROL);
                    
                    if (hasControlEffect)
                    {
                        return Result<bool>.Failure("Cannot perform action: hero is under control effect (stunned, frozen, etc.)");
                    }
                }
            }
        }
        
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
        
        // Calcular dano usando pipeline (se disponível)
        float damageDealt;
        if (_damageCalculator != null)
        {
            var actionDef = new ActionDefinition
            {
                ActionId = "basic_attack",
                Tags = new List<string> { "physical", "melee", "can_crit" },
                Effects = new List<EffectDefinition>
                {
                    new EffectDefinition
                    {
                        Type = EffectType.DAMAGE,
                        FlatValue = BASIC_ATTACK_DAMAGE,
                        Target = EffectTarget.TARGET
                    }
                }
            };
            
            var damageResult = _damageCalculator.CalculateDamage(actionDef, state.Hero, target);
            damageDealt = damageResult.FinalDamage;
            
            _logger.LogDebug($"Basic attack damage: {damageDealt:F2} (crit tier: {damageResult.CritTier})");
        }
        else
        {
            // Fallback para dano fixo
            damageDealt = BASIC_ATTACK_DAMAGE;
        }
        
        // Processar status effects ON_DAMAGE_TAKEN (THORNS, SHIELD, etc.)
        var (modifiedDamage, updatedHero) = ProcessOnDamageTakenEffects(target, state.Hero, damageDealt, state.CurrentTurn);
        
        var newTarget = ApplyDamageWithBufferCheck(target, modifiedDamage);
        
        // Ganhar energia
        var energyPool = updatedHero.GetResource("energy")!;
        var newEnergyPool = energyPool.Gain(BASIC_ATTACK_ENERGY_GAIN);
        var newHero = updatedHero.UpdateResource("energy", newEnergyPool);
        
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = state.Hero.EntityId,
            ActionType = ActionType.BASIC_ATTACK,
            TargetId = targetId,
            DamageDealt = (int)damageDealt,
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
        
        // Calcular dano usando pipeline (se disponível)
        float damageDealt;
        if (_damageCalculator != null)
        {
            var actionDef = new ActionDefinition
            {
                ActionId = powerId,
                Tags = new List<string> { "spell", "fire", "can_crit" },
                Effects = new List<EffectDefinition>
                {
                    new EffectDefinition
                    {
                        Type = EffectType.DAMAGE,
                        FlatValue = DEFAULT_POWER_DAMAGE,
                        Target = EffectTarget.TARGET
                    }
                }
            };
            
            var damageResult = _damageCalculator.CalculateDamage(actionDef, state.Hero, target);
            damageDealt = damageResult.FinalDamage;
            
            _logger.LogDebug($"Power {powerId} damage: {damageDealt:F2} (crit tier: {damageResult.CritTier})");
        }
        else
        {
            // Fallback para dano fixo
            damageDealt = DEFAULT_POWER_DAMAGE;
        }
        
        // Processar status effects ON_DAMAGE_TAKEN (THORNS, SHIELD, etc.)
        var (modifiedDamage, updatedHero) = ProcessOnDamageTakenEffects(target, state.Hero, damageDealt, state.CurrentTurn);
        
        var newTarget = ApplyDamageWithBufferCheck(target, modifiedDamage);
        
        // Gastar energia
        var energyPool = updatedHero.GetResource("energy")!;
        var newEnergyPool = energyPool.Spend(DEFAULT_POWER_COST);
        var newHero = updatedHero.UpdateResource("energy", newEnergyPool);
        
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = state.Hero.EntityId,
            ActionType = ActionType.POWER,
            PowerId = powerId,
            TargetId = targetId,
            DamageDealt = (int)damageDealt,
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
        
        // Incrementar turno primeiro
        var updatedState = state with
        {
            ActionHistory = newHistory,
            CurrentTurn = state.CurrentTurn + 1
        };
        
        if (_statusEffectManager != null)
        {
            // Processar START_OF_TURN (regeneração, energia, verificar stun)
            updatedState = ProcessStartOfTurnStatusEffects(updatedState);
            
            // Processar END_OF_TURN (DoT, decrementar durações)
            updatedState = ProcessEndOfTurnStatusEffects(updatedState);
        }
        
        return updatedState;
    }
    
    /// <summary>
    /// Processa status effects no início do turno (Regeneração, Energia, verificar Stun)
    /// </summary>
    private CombatState ProcessStartOfTurnStatusEffects(CombatState state)
    {
        _logger.LogDebug("Processing start-of-turn status effects");
        
        var updatedHero = state.Hero;
        var updatedEnemies = state.Enemies.ToList();
        
        // Processar status effects do herói
        if (Guid.TryParse(state.Hero.EntityId, out var heroGuid))
        {
            var processResult = _statusEffectManager!.ProcessStatusEffects(heroGuid, StatusEffectTiming.START_OF_TURN, state.CurrentTurn);
            if (processResult.IsSuccess)
            {
                updatedHero = ApplyStatusEffectResults(updatedHero, processResult.Value.TickResults);
            }
        }
        
        // Processar status effects dos inimigos
        for (int i = 0; i < updatedEnemies.Count; i++)
        {
            var enemy = updatedEnemies[i];
            if (Guid.TryParse(enemy.EntityId, out var enemyGuid))
            {
                var processResult = _statusEffectManager!.ProcessStatusEffects(enemyGuid, StatusEffectTiming.START_OF_TURN, state.CurrentTurn);
                if (processResult.IsSuccess)
                {
                    updatedEnemies[i] = ApplyStatusEffectResults(enemy, processResult.Value.TickResults);
                }
            }
        }
        
        return state with
        {
            Hero = updatedHero,
            Enemies = updatedEnemies
        };
    }
    
    /// <summary>
    /// Processa status effects no final do turno (DoT, HoT, decrementar durações)
    /// </summary>
    private CombatState ProcessEndOfTurnStatusEffects(CombatState state)
    {
        _logger.LogDebug("Processing end-of-turn status effects");
        
        var updatedHero = state.Hero;
        var updatedEnemies = state.Enemies.ToList();
        
        // Processar status effects do herói
        if (Guid.TryParse(state.Hero.EntityId, out var heroGuid))
        {
            var processResult = _statusEffectManager!.ProcessStatusEffects(heroGuid, StatusEffectTiming.END_OF_TURN, state.CurrentTurn);
            if (processResult.IsSuccess)
            {
                updatedHero = ApplyStatusEffectResults(updatedHero, processResult.Value.TickResults);
            }
            
            // Decrementar durações
            _statusEffectManager.TickDurations(heroGuid);
        }
        
        // Processar status effects dos inimigos
        for (int i = 0; i < updatedEnemies.Count; i++)
        {
            var enemy = updatedEnemies[i];
            if (Guid.TryParse(enemy.EntityId, out var enemyGuid))
            {
                var processResult = _statusEffectManager!.ProcessStatusEffects(enemyGuid, StatusEffectTiming.END_OF_TURN, state.CurrentTurn);
                if (processResult.IsSuccess)
                {
                    updatedEnemies[i] = ApplyStatusEffectResults(enemy, processResult.Value.TickResults);
                }
                
                // Decrementar durações
                _statusEffectManager.TickDurations(enemyGuid);
            }
        }
        
        return state with
        {
            Hero = updatedHero,
            Enemies = updatedEnemies
        };
    }
    
    /// <summary>
    /// Aplica os resultados de status effects a uma entidade (dano, cura, etc.)
    /// </summary>
    private CombatEntity ApplyStatusEffectResults(CombatEntity entity, List<StatusEffectTickResult> results)
    {
        var updatedEntity = entity;
        
        foreach (var result in results)
        {
            // StatusEffectTickResult usa Value para representar o valor aplicado
            // Verificar se é DoT (dano) ou HoT (cura) baseado no tipo específico
            
            // DoTs: BURNING, POISON, BLEEDING
            if (result.Type == StatusEffectType.BURNING || 
                result.Type == StatusEffectType.POISON || 
                result.Type == StatusEffectType.BLEEDING)
            {
                if (result.Value > 0)
                {
                    _logger.LogDebug($"Status effect {result.StatusId} dealt {result.Value} damage to {entity.EntityId}");
                    updatedEntity = ApplyDamageWithBufferCheck(updatedEntity, result.Value);
                }
            }
            // HoT: REGENERATION
            else if (result.Type == StatusEffectType.REGENERATION)
            {
                if (result.Value > 0)
                {
                    _logger.LogDebug($"Status effect {result.StatusId} healed {result.Value} to {entity.EntityId}");
                    var healthPool = updatedEntity.GetResource("health");
                    if (healthPool != null)
                    {
                        var newHealthPool = healthPool.Gain(result.Value);
                        updatedEntity = updatedEntity.UpdateResource("health", newHealthPool);
                    }
                }
            }
        }
        
        return updatedEntity;
    }
    
    /// <summary>
    /// Processa status effects quando uma entidade recebe dano (THORNS, SHIELD, etc.)
    /// Retorna o dano modificado e a entidade atacante atualizada (para THORNS)
    /// </summary>
    private (float modifiedDamage, CombatEntity updatedAttacker) ProcessOnDamageTakenEffects(
        CombatEntity target, 
        CombatEntity attacker, 
        float incomingDamage,
        int currentTurn)
    {
        if (_statusEffectManager == null || !Guid.TryParse(target.EntityId, out var targetGuid))
        {
            return (incomingDamage, attacker);
        }
        
        var processResult = _statusEffectManager.ProcessStatusEffects(targetGuid, StatusEffectTiming.ON_DAMAGE_TAKEN, currentTurn);
        if (processResult.IsFailure)
        {
            return (incomingDamage, attacker);
        }
        
        var modifiedDamage = incomingDamage;
        var updatedAttacker = attacker;
        
        foreach (var result in processResult.Value.TickResults)
        {
            // SHIELD: Absorve dano
            if (result.Type == StatusEffectType.SHIELD && result.Value > 0)
            {
                var absorbed = System.Math.Min(modifiedDamage, result.Value);
                modifiedDamage -= absorbed;
                _logger.LogDebug($"SHIELD absorbed {absorbed} damage, remaining damage: {modifiedDamage}");
            }
            // THORNS: Reflete dano ao atacante
            else if (result.Type == StatusEffectType.THORNS && result.Value > 0)
            {
                _logger.LogDebug($"THORNS reflected {result.Value} damage to {attacker.EntityId}");
                updatedAttacker = ApplyDamageWithBufferCheck(updatedAttacker, result.Value);
            }
            // INTANGIBLE: Limita dano máximo recebido
            else if (result.Type == StatusEffectType.INTANGIBLE && result.Value > 0)
            {
                if (modifiedDamage > result.Value)
                {
                    var capped = modifiedDamage - result.Value;
                    modifiedDamage = result.Value;
                    _logger.LogDebug($"INTANGIBLE capped {capped} damage, damage limited to: {modifiedDamage}");
                }
            }
        }
        
        return (modifiedDamage, updatedAttacker);
    }
    
    /// <summary>
    /// Aplica dano a uma entidade, verificando BUFFER para prevenir morte
    /// Retorna a entidade atualizada
    /// </summary>
    private CombatEntity ApplyDamageWithBufferCheck(CombatEntity entity, float damage)
    {
        if (_statusEffectManager == null || !Guid.TryParse(entity.EntityId, out var entityGuid))
        {
            return entity.TakeDamage(damage);
        }
        
        var healthPool = entity.GetResource("health");
        if (healthPool == null)
        {
            return entity;
        }
        
        // Verificar se o dano seria fatal
        var wouldDie = (healthPool.Current - damage) <= 0;
        
        if (wouldDie)
        {
            // Verificar se há BUFFER ativo
            var activeStatusResult = _statusEffectManager.GetActiveStatus(entityGuid);
            if (activeStatusResult.IsSuccess)
            {
                var bufferEffect = activeStatusResult.Value.FirstOrDefault(s => 
                    s.Definition.Type == StatusEffectType.BUFFER);
                
                if (bufferEffect != null)
                {
                    _logger.LogDebug($"BUFFER prevented death for {entity.EntityId}, leaving at 1 HP");
                    
                    // Remover o BUFFER
                    _statusEffectManager.RemoveStatus(entityGuid, bufferEffect.InstanceId);
                    
                    // Deixar a entidade com 1 HP
                    var newHealthPool = healthPool.Set(1);
                    return entity.UpdateResource("health", newHealthPool);
                }
            }
        }
        
        // Sem BUFFER ou dano não-fatal: aplicar dano normalmente
        return entity.TakeDamage(damage);
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
