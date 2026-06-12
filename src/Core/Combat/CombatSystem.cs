using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Common;
using Core.Damage;
using Core.Effects;
using Core.Entity.Definitions;
using Core.Entity.Integration;
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
    private readonly IResourceRegenerationProcessor? _regenerationProcessor;
     private readonly ITurnOrderCalculator _turnOrderCalculator;
    private readonly IActionManager? _actionManager;
    private readonly IEffectResolver? _effectResolver;
    private readonly IActionCostEvaluator? _actionCostEvaluator;
    private readonly EntityDefinitionLoader? _entityDefinitionLoader;
    private readonly EntityCombatAdapter _entityAdapter;
    
    private const string BasicAttackActionId = "basic_attack";
    
    public CombatSystem(
        ILogger logger, 
        IResourceManager resourceManager,
        ITurnOrderCalculator turnOrderCalculator,
        IEventBus? eventBus = null, 
        IDamageCalculator? damageCalculator = null,
        IStatusEffectManager? statusEffectManager = null,
        IResourceRegenerationProcessor? regenerationProcessor = null,
        IActionManager? actionManager = null,
        EntityDefinitionLoader? entityDefinitionLoader = null,
        IEffectResolver? effectResolver = null,
        IActionCostEvaluator? actionCostEvaluator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _turnOrderCalculator = turnOrderCalculator ?? throw new ArgumentNullException(nameof(turnOrderCalculator));
        _eventBus = eventBus;
        _damageCalculator = damageCalculator;
        _statusEffectManager = statusEffectManager;
        _regenerationProcessor = regenerationProcessor;
        _actionManager = actionManager;
        _effectResolver = effectResolver;
        _actionCostEvaluator = actionCostEvaluator;
        _entityDefinitionLoader = entityDefinitionLoader;
        _entityAdapter = new EntityCombatAdapter(_resourceManager);
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
            
            var hero = CreateHeroCombatEntity(heroId, initialEnergy);
            var enemies = enemyIds.Select(CreateEnemyCombatEntity).ToList();
            
            // Criar estado inicial
            var combatState = new CombatState
            {
                Hero = hero,
                Enemies = enemies,
                CurrentTurn = 1,
                Status = CombatStatus.ACTIVE
            };
            
            // Inicializar calculadora de ordem de turnos e calcular ordem inicial
            var initResult = _turnOrderCalculator.Initialize(combatState);
            if (initResult.IsFailure)
            {
                _logger.LogWarning($"Failed to initialize turn order calculator: {initResult.Error}");
            }
            else
            {
                var turnOrderResult = _turnOrderCalculator.CalculateTurnOrder(combatState);
                if (turnOrderResult.IsSuccess)
                {
                    combatState = combatState with { TurnOrder = turnOrderResult.Value };
                    _logger.LogDebug($"Initial turn order: {string.Join(", ", turnOrderResult.Value)}");
                }
            }
            
            // Adicionar ao dicionário
            if (!_activeCombats.TryAdd(combatState.CombatId, combatState))
                return Result<CombatState>.Failure("Failed to create combat (ID collision)");
            
            // Publicar evento
            _eventBus?.Publish(new CombatStartedEvent
            {
                CombatId = combatState.CombatId,
                HeroId = heroId,
                EnemyIds = enemyIds,
                InitialEnergy = (int)(hero.GetResource("energy")?.Current ?? initialEnergy),
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
    
    public Result<CombatState> StartCombatWithEntities(Entity.Entity hero, List<Entity.Entity> enemies)
    {
        try
        {
            // Validações
            if (hero == null)
                return Result<CombatState>.Failure("Hero entity cannot be null");
            
            if (enemies == null || enemies.Count == 0)
                return Result<CombatState>.Failure("At least one enemy is required");
            
            // Converter entidades para CombatEntity usando o adapter
            var heroCombat = _entityAdapter.ToCombatEntity(hero);
            var enemiesCombat = _entityAdapter.ToCombatEntities(enemies);
            
            // Criar estado inicial
            var combatState = new CombatState
            {
                Hero = heroCombat,
                Enemies = enemiesCombat.ToList(),
                CurrentTurn = 1,
                Status = CombatStatus.ACTIVE
            };
            
            // Inicializar calculadora de ordem de turnos e calcular ordem inicial
            var initResult = _turnOrderCalculator.Initialize(combatState);
            if (initResult.IsFailure)
            {
                _logger.LogWarning($"Failed to initialize turn order calculator: {initResult.Error}");
            }
            else
            {
                var turnOrderResult = _turnOrderCalculator.CalculateTurnOrder(combatState);
                if (turnOrderResult.IsSuccess)
                {
                    combatState = combatState with { TurnOrder = turnOrderResult.Value };
                    _logger.LogDebug($"Initial turn order: {string.Join(", ", turnOrderResult.Value)}");
                }
            }
            
            // Adicionar ao dicionário
            if (!_activeCombats.TryAdd(combatState.CombatId, combatState))
                return Result<CombatState>.Failure("Failed to create combat (ID collision)");
            
            // Publicar evento
            _eventBus?.Publish(new CombatStartedEvent
            {
                CombatId = combatState.CombatId,
                HeroId = hero.EntityId,
                EnemyIds = enemies.Select(e => e.EntityId).ToList(),
                InitialEnergy = (int)(heroCombat.GetResource("energy")?.Current ?? 0),
                Target = combatState.CombatId.ToString()
            });
            
            _logger.LogInformation($"Combat started with entities: {combatState.CombatId}");
            return Result<CombatState>.Success(combatState);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error starting combat with entities: {ex.Message}");
            return Result<CombatState>.Failure($"Failed to start combat: {ex.Message}");
        }
    }
    
    public Result<CombatState> ExecuteAction(Guid combatId, CombatActionCommand command)
    {
        try
        {
            // Obter estado atual
            if (!_activeCombats.TryGetValue(combatId, out var currentState))
                return Result<CombatState>.Failure($"Combat {combatId} not found");
            
            if (!currentState.IsActive)
                return Result<CombatState>.Failure($"Combat {combatId} is not active (status: {currentState.Status})");

            var actor = currentState.GetEntity(command.ActorId);
            if (actor == null)
                return Result<CombatState>.Failure($"Actor {command.ActorId} not found");

            if (!actor.IsAlive)
                return Result<CombatState>.Failure($"Actor {command.ActorId} is already dead");
            
            // Validar ação
            var validationResult = ValidateAction(currentState, actor, command);
            if (validationResult.IsFailure)
                return Result<CombatState>.Failure(validationResult.Error);
            
            // Executar ação e criar novo estado
            var newState = command.ActionType switch
            {
                ActionType.BASIC_ATTACK => ExecuteConfiguredAction(currentState, actor, ActionType.BASIC_ATTACK, BasicAttackActionId, command.TargetId!, command.CostOptionId, command.RunModifiers),
                ActionType.POWER => ExecuteConfiguredAction(currentState, actor, ActionType.POWER, command.PowerId!, command.TargetId!, command.CostOptionId, command.RunModifiers),
                ActionType.PASS => ExecutePass(currentState, actor),
                ActionType.END_TURN => ExecuteEndTurn(currentState, actor),
                _ => throw new InvalidOperationException($"Unknown action type: {command.ActionType}")
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
    
    private Result<bool> ValidateAction(CombatState state, CombatEntity actor, CombatActionCommand command)
    {
        // Verificar se o ator está sob controle (STUN, FREEZE, etc.)
        if (command.ActionType != ActionType.PASS && command.ActionType != ActionType.END_TURN)
        {
            if (_statusEffectManager != null && Guid.TryParse(actor.EntityId, out var actorGuid))
            {
                var activeStatusResult = _statusEffectManager.GetActiveStatus(actorGuid);
                if (activeStatusResult.IsSuccess)
                {
                    var hasControlEffect = activeStatusResult.Value.Any(s => 
                        s.Definition.Behavior == StatusEffectBehavior.CONTROL);
                    
                    if (hasControlEffect)
                    {
                        return Result<bool>.Failure($"Cannot perform action: actor {actor.EntityId} is under control effect (stunned, frozen, etc.)");
                    }
                }
            }
        }
        
        switch (command.ActionType)
        {
            case ActionType.BASIC_ATTACK:
                if (string.IsNullOrWhiteSpace(command.TargetId))
                    return Result<bool>.Failure("Target is required for basic attack");
                if (state.GetEntity(command.TargetId) == null)
                    return Result<bool>.Failure($"Target {command.TargetId} not found");
                if (!state.GetEntity(command.TargetId)!.IsAlive)
                    return Result<bool>.Failure($"Target {command.TargetId} is already dead");

                var basicActionDefinition = GetConfiguredAction(BasicAttackActionId);
                if (basicActionDefinition == null)
                    return Result<bool>.Failure($"Action definition not found: {BasicAttackActionId}");

                var basicAffordabilityError = ValidateActionCosts(actor, basicActionDefinition.Costs, command.CostOptionId);
                if (basicAffordabilityError != null)
                    return Result<bool>.Failure(basicAffordabilityError);
                break;
                
            case ActionType.POWER:
                if (string.IsNullOrWhiteSpace(command.PowerId))
                    return Result<bool>.Failure("Power ID is required");
                if (string.IsNullOrWhiteSpace(command.TargetId))
                    return Result<bool>.Failure("Target is required for power");

                var actionDefinition = GetConfiguredAction(command.PowerId);
                if (actionDefinition == null)
                    return Result<bool>.Failure($"Action definition not found: {command.PowerId}");

                var affordabilityError = ValidateActionCosts(actor, actionDefinition.Costs, command.CostOptionId);
                if (affordabilityError != null)
                    return Result<bool>.Failure(affordabilityError);
                
                if (state.GetEntity(command.TargetId) == null)
                    return Result<bool>.Failure($"Target {command.TargetId} not found");
                if (!state.GetEntity(command.TargetId)!.IsAlive)
                    return Result<bool>.Failure($"Target {command.TargetId} is already dead");
                break;
                
            case ActionType.PASS:
            case ActionType.END_TURN:
                // Sempre válido
                break;
                
            default:
                return Result<bool>.Failure($"Unknown action type: {command.ActionType}");
        }
        
        return Result<bool>.Success(true);
    }

    private CombatEntity CreateHeroCombatEntity(string heroId, int initialEnergy)
    {
        var definition = TryLoadEntityDefinition(heroId);
        if (definition != null)
        {
            var hero = _entityAdapter.CreateCombatEntityFromDefinition(heroId, definition);
            var energyPool = hero.GetResource("energy");
            return energyPool == null
                ? hero
                : hero.UpdateResource("energy", energyPool.Set(initialEnergy));
        }

        var heroHealthPool = _resourceManager.CreatePool("health", 100);
        var heroEnergyPool = _resourceManager.CreatePool("energy", initialEnergy);
        var heroResources = new Dictionary<string, ResourcePool>
        {
            ["health"] = heroHealthPool,
            ["energy"] = heroEnergyPool
        };

        return new CombatEntity
        {
            EntityId = heroId,
            Name = "Hero",
            IsHero = true,
            ResourceState = new EntityResourceState
            {
                EntityId = heroId,
                Resources = heroResources
            }
        };
    }

    private CombatEntity CreateEnemyCombatEntity(string enemyId)
    {
        var definition = TryLoadEntityDefinition(enemyId);
        if (definition != null)
            return _entityAdapter.CreateCombatEntityFromDefinition(enemyId, definition);

        var enemyHealthPool = _resourceManager.CreatePool("health", 50);
        var enemyResources = new Dictionary<string, ResourcePool>
        {
            ["health"] = enemyHealthPool
        };

        return new CombatEntity
        {
            EntityId = enemyId,
            Name = enemyId,
            IsHero = false,
            ResourceState = new EntityResourceState
            {
                EntityId = enemyId,
                Resources = enemyResources
            }
        };
    }

    private EntityDefinition? TryLoadEntityDefinition(string definitionId)
    {
        if (_entityDefinitionLoader == null)
            return null;

        var result = _entityDefinitionLoader.LoadDefinition(definitionId);
        if (result.IsSuccess)
            return result.Value;

        _logger.LogDebug($"Entity definition not found for combat start: {definitionId}");
        return null;
    }
    
    private CombatState ExecuteConfiguredAction(
        CombatState state,
        CombatEntity actor,
        ActionType actionType,
        string actionId,
        string targetId,
        string? costOptionId,
        IReadOnlyDictionary<string, float>? runModifiers)
    {
        var target = state.GetEntity(targetId)!;
        var actionDefinition = GetConfiguredAction(actionId)
            ?? throw new InvalidOperationException($"Action definition not found: {actionId}");

        var damageDealt = ApplyRunModifiers(CalculateActionDamage(actionDefinition, actor, target), actionDefinition, runModifiers);

        // Processar status effects ON_DAMAGE_TAKEN por comportamento configurado.
        var (modifiedDamage, updatedActor) = ProcessOnDamageTakenEffects(target, actor, damageDealt, state.CurrentTurn);
        var newTarget = ApplyDamageWithBufferCheck(target, modifiedDamage);

        updatedActor = ApplyCosts(updatedActor, actionDefinition.Costs, costOptionId);
        updatedActor = ApplyActionResourceEffects(updatedActor, newTarget, actionDefinition.Effects);

        var previousEnergy = actor.GetResource("energy")?.Current ?? 0;
        var currentEnergy = updatedActor.GetResource("energy")?.Current ?? previousEnergy;
        var energyChange = (int)(currentEnergy - previousEnergy);
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = actor.EntityId,
            ActionType = actionType,
            PowerId = actionType == ActionType.POWER ? actionId : null,
            TargetId = targetId,
            DamageDealt = (int)damageDealt,
            EnergyChange = energyChange
        };

        var newHistory = state.ActionHistory.Append(action).ToList();

        PublishEnergyChange(state, actor.EntityId, previousEnergy, currentEnergy, energyChange, $"Action: {actionId}");

        var updatedState = state;
        if (updatedActor.EntityId == newTarget.EntityId)
        {
            updatedState = updatedState.ReplaceEntity(updatedActor);
        }
        else
        {
            updatedState = updatedState.ReplaceEntity(updatedActor).ReplaceEntity(newTarget);
        }

        return updatedState with { ActionHistory = newHistory };
    }

    private float CalculateActionDamage(ActionDefinition actionDefinition, CombatEntity actor, CombatEntity target)
    {
        var damageEffects = actionDefinition.Effects.Where(e => e.Type == EffectType.DAMAGE).ToList();
        if (damageEffects.Count == 0)
            return 0;

        if (_effectResolver != null)
        {
            return damageEffects.Sum(effect => ResolveActionEffectValue(effect, actionDefinition.ActionId, actor, target));
        }

        if (_damageCalculator != null)
        {
            var damageResult = _damageCalculator.CalculateDamage(actionDefinition, actor, target);
            _logger.LogDebug($"Action {actionDefinition.ActionId} damage: {damageResult.FinalDamage:F2} (crit tier: {damageResult.CritTier})");
            return damageResult.FinalDamage;
        }

        return damageEffects.Sum(e => e.FlatValue ?? 0);
    }

    private static float ApplyRunModifiers(float baseValue, ActionDefinition actionDefinition, IReadOnlyDictionary<string, float>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0 || baseValue == 0)
            return baseValue;

        var value = baseValue;
        if (modifiers.TryGetValue("added_damage", out var addedDamage))
            value += addedDamage;

        if (modifiers.TryGetValue("increased_damage_total", out var increasedDamage))
            value *= 1 + increasedDamage;

        return actionDefinition.Effects.Any(effect => effect.Type == EffectType.DAMAGE) ? value : baseValue;
    }

    private float ResolveActionEffectValue(EffectDefinition effect, string actionId, CombatEntity actor, CombatEntity target)
    {
        var instance = CreateActionEffectInstance(effect, actionId, actor.EntityId, target.EntityId);
        var context = new CombatEffectContext
        {
            CombatState = new CombatState { Hero = actor, Enemies = new List<CombatEntity> { target } },
            SourceEntityId = actor.EntityId,
            TargetEntityId = target.EntityId,
            SourceActionId = actionId
        };

        var result = _effectResolver!.ApplyEffect(instance, context);
        if (result.IsSuccess && result.Value.Success && result.Value.EffectResult.ValueApplied.HasValue)
            return result.Value.EffectResult.ValueApplied.Value;

        _logger.LogWarning($"Effect resolver failed for action {actionId}: {(result.IsFailure ? result.Error : result.Value.ErrorMessage)}. Falling back to flat value.");
        return effect.FlatValue ?? 0;
    }

    private CombatEntity ApplyActionResourceEffects(
        CombatEntity actor,
        CombatEntity target,
        IEnumerable<EffectDefinition> effects)
    {
        var updatedActor = actor;

        foreach (var effect in effects.Where(e => e.Type == EffectType.MODIFY_RESOURCE))
        {
            var value = ResolveResourceEffectValue(effect, updatedActor, target);
            var resourceId = effect.TargetResource;
            if (string.IsNullOrWhiteSpace(resourceId) || value == 0)
                continue;

            if (effect.Target == EffectTarget.SELF)
            {
                var pool = updatedActor.GetResource(resourceId);
                if (pool != null)
                    updatedActor = updatedActor.UpdateResource(resourceId, ApplyResourceDelta(pool, value));
            }
            else if (effect.Target == EffectTarget.TARGET && target.EntityId == actor.EntityId)
            {
                var pool = updatedActor.GetResource(resourceId);
                if (pool != null)
                    updatedActor = updatedActor.UpdateResource(resourceId, ApplyResourceDelta(pool, value));
            }
        }

        return updatedActor;
    }

    private float ResolveResourceEffectValue(EffectDefinition effect, CombatEntity actor, CombatEntity target)
    {
        if (_effectResolver == null)
            return effect.FlatValue ?? 0;

        var instance = CreateActionEffectInstance(effect, string.Empty, actor.EntityId, target.EntityId);
        var context = new CombatEffectContext
        {
            CombatState = new CombatState { Hero = actor, Enemies = new List<CombatEntity> { target } },
            SourceEntityId = actor.EntityId,
            TargetEntityId = target.EntityId
        };

        var result = _effectResolver.ApplyEffect(instance, context);
        if (result.IsSuccess && result.Value.Success && result.Value.EffectResult.ValueApplied.HasValue)
            return result.Value.EffectResult.ValueApplied.Value;

        _logger.LogWarning($"Effect resolver failed for resource effect: {(result.IsFailure ? result.Error : result.Value.ErrorMessage)}. Falling back to flat value.");
        return effect.FlatValue ?? 0;
    }

    private static EffectInstance CreateActionEffectInstance(EffectDefinition effect, string sourceActionId, string actorId, string targetId)
    {
        return new EffectInstance
        {
            Definition = effect,
            SourceEntityId = actorId,
            TargetEntityId = targetId,
            SourceActionId = sourceActionId
        };
    }

    private static ResourcePool ApplyResourceDelta(ResourcePool pool, float value)
    {
        return value >= 0 ? pool.Gain(value) : pool.Spend(-value);
    }

    private void PublishEnergyChange(CombatState state, string actorId, float oldEnergy, float newEnergy, int energyChange, string reason)
    {
        if (energyChange == 0)
            return;

        _eventBus?.Publish(new EnergyChangedEvent
        {
            CombatId = state.CombatId,
            OldEnergy = (int)oldEnergy,
            NewEnergy = (int)newEnergy,
            Delta = energyChange,
            Reason = reason,
            Turn = state.CurrentTurn,
            Target = actorId
        });
    }
    
    private CombatState ExecutePass(CombatState state, CombatEntity actor)
    {
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = actor.EntityId,
            ActionType = ActionType.PASS
        };
        
        var newHistory = state.ActionHistory.Append(action).ToList();
        
        return state with { ActionHistory = newHistory };
    }
    
    private CombatState ExecuteEndTurn(CombatState state, CombatEntity actor)
    {
        var action = new CombatAction
        {
            Turn = state.CurrentTurn,
            ActorId = actor.EntityId,
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
        
        // Processar regeneração de recursos
        updatedState = ProcessEndOfTurnRegeneration(updatedState);
        updatedState = ProcessStartOfTurnRegeneration(updatedState);
        
        // Recalcular ordem de turnos para o próximo turno
         var turnOrderResult = _turnOrderCalculator.CalculateTurnOrder(updatedState);
         if (turnOrderResult.IsSuccess)
         {
             updatedState = updatedState with { TurnOrder = turnOrderResult.Value };
             _logger.LogDebug($"Turn order for turn {updatedState.CurrentTurn}: {string.Join(", ", turnOrderResult.Value)}");
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
            if (result.Behavior == StatusEffectBehavior.DAMAGE_OVER_TIME)
            {
                if (result.Value > 0)
                {
                    _logger.LogDebug($"Status effect {result.StatusId} dealt {result.Value} damage to {entity.EntityId}");
                    updatedEntity = ApplyDamageWithBufferCheck(updatedEntity, result.Value);
                }
            }
            else if (result.Behavior == StatusEffectBehavior.HEAL_OVER_TIME)
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
    /// Processa status effects quando uma entidade recebe dano.
    /// Retorna o dano modificado e a entidade atacante atualizada para efeitos reativos.
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
            if (result.Behavior == StatusEffectBehavior.SHIELD && result.Value > 0)
            {
                var absorbed = System.Math.Min(modifiedDamage, result.Value);
                modifiedDamage -= absorbed;
                _logger.LogDebug($"SHIELD absorbed {absorbed} damage, remaining damage: {modifiedDamage}");
            }
            else if (result.Behavior == StatusEffectBehavior.REACTIVE && result.Value > 0)
            {
                _logger.LogDebug($"Reactive status {result.StatusId} applied {result.Value} damage to {attacker.EntityId}");
                updatedAttacker = ApplyDamageWithBufferCheck(updatedAttacker, result.Value);
            }
            else if (result.Behavior == StatusEffectBehavior.DAMAGE_CAP && result.Value > 0)
            {
                if (modifiedDamage > result.Value)
                {
                    var capped = modifiedDamage - result.Value;
                    modifiedDamage = result.Value;
                    _logger.LogDebug($"Status effect {result.StatusId} capped {capped} damage, damage limited to: {modifiedDamage}");
                }
            }
        }
        
        return (modifiedDamage, updatedAttacker);
    }
    
    /// <summary>
    /// Aplica dano a uma entidade, verificando status de prevenção de morte
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
            // Verificar se há status ativo com comportamento de prevenção de morte
            var activeStatusResult = _statusEffectManager.GetActiveStatus(entityGuid);
            if (activeStatusResult.IsSuccess)
            {
                var bufferEffect = activeStatusResult.Value.FirstOrDefault(s => 
                    s.Definition.Behavior == StatusEffectBehavior.DEATH_PREVENTION);
                
                if (bufferEffect != null)
                {
                    _logger.LogDebug($"Status effect {bufferEffect.StatusId} prevented death for {entity.EntityId}, leaving at 1 HP");
                    
                    // Consumir a prevenção de morte
                    _statusEffectManager.RemoveStatus(entityGuid, bufferEffect.InstanceId);
                    
                    // Deixar a entidade com 1 HP
                    var newHealthPool = healthPool.Set(1);
                    return entity.UpdateResource("health", newHealthPool);
                }
            }
        }
        
        // Sem prevenção de morte ou dano não-fatal: aplicar dano normalmente
        return entity.TakeDamage(damage);
    }
    
    /// <summary>
    /// Aplica custos de uma ação ao ator.
    /// Se costOptionId for fornecido, aplica custos da opção alternativa.
    /// Caso contrário, aplica custos normais.
    /// </summary>
    private CombatEntity ApplyCosts(
        CombatEntity actor, 
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
                var pool = actor.GetResource(cost.ResourceId);
                if (pool == null)
                    throw new InvalidOperationException($"Resource not found: {cost.ResourceId}");
                
                var newPool = SpendCost(cost, pool, actor.ResourceState.Resources);
                updates[cost.ResourceId] = newPool;
            }
        }
        else
        {
            // Aplicar custos normais
            foreach (var cost in costs.Costs)
            {
                var pool = actor.GetResource(cost.ResourceId);
                if (pool == null)
                    throw new InvalidOperationException($"Resource not found: {cost.ResourceId}");
                
                var newPool = SpendCost(cost, pool, actor.ResourceState.Resources);
                updates[cost.ResourceId] = newPool;
            }
        }
        
        return actor.UpdateResources(updates);
    }

    private ActionDefinition? GetConfiguredAction(string actionId)
    {
        if (_actionManager == null)
            return null;

        var result = _actionManager.GetDefinition(actionId);
        return result.IsSuccess ? result.Value : null;
    }

    private ResourcePool SpendCost(ResourceCost cost, ResourcePool pool, IReadOnlyDictionary<string, ResourcePool> resources)
    {
        if (_actionCostEvaluator == null)
            return pool.Spend(cost.Amount);

        var spend = _actionCostEvaluator.Spend(cost, pool, resources);
        if (spend.IsFailure)
            throw new InvalidOperationException(spend.Error);

        return spend.Value;
    }

    private string? ValidateActionCosts(CombatEntity actor, ActionCosts costs, string? costOptionId)
    {
        var resources = new Dictionary<string, ResourcePool>(actor.ResourceState.Resources);

        if (costs.AlternativeCosts.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(costOptionId))
                return "Cost option must be specified for this action";

            var option = costs.GetOption(costOptionId);
            if (option == null)
                return $"Cost option not found: {costOptionId}";

            return GetCostError(option.Costs, resources);
        }

        return GetCostError(costs.Costs, resources);
    }

    private string? GetCostError(IReadOnlyList<ResourceCost> costs, IReadOnlyDictionary<string, ResourcePool> resources)
    {
        foreach (var cost in costs)
        {
            if (!resources.TryGetValue(cost.ResourceId, out var pool))
                return $"Resource not found: {cost.ResourceId}";

            if (_actionCostEvaluator == null)
            {
                if (!cost.AllowOverdraft && !pool.CanAfford(cost.Amount))
                    return $"Insufficient {pool.Definition?.DisplayName ?? cost.ResourceId}: has {pool.Current}, needs {cost.Amount}";
                continue;
            }

            var amount = _actionCostEvaluator.CalculateCost(cost, resources);
            if (amount.IsFailure)
                return amount.Error;

            if (!cost.AllowOverdraft && !pool.CanAfford(amount.Value))
                return $"Insufficient {pool.Definition?.DisplayName ?? cost.ResourceId}: has {pool.Current}, needs {amount.Value}";
        }

        return null;
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

    public Result<CombatState> UpdateCombatState(Guid combatId, Func<CombatState, CombatState> update)
    {
        if (update == null)
            return Result<CombatState>.Failure("Combat state update is required");

        try
        {
            while (true)
            {
                if (!_activeCombats.TryGetValue(combatId, out var currentState))
                    return Result<CombatState>.Failure($"Combat {combatId} not found");

                var updatedState = update(currentState);
                if (updatedState == null)
                    return Result<CombatState>.Failure("Combat state update returned null");

                if (_activeCombats.TryUpdate(combatId, updatedState, currentState))
                    return Result<CombatState>.Success(updatedState);
            }
        }
        catch (Exception ex)
        {
            return Result<CombatState>.Failure($"Failed to update combat state: {ex.Message}");
        }
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
    
    /// <summary>
    /// Processa regeneração de recursos no início do turno
    /// </summary>
    private CombatState ProcessStartOfTurnRegeneration(CombatState state)
    {
        if (_regenerationProcessor == null)
            return state;
        
        _logger.LogDebug("Processing start-of-turn resource regeneration");
        
        var updatedHero = state.Hero;
        var updatedEnemies = state.Enemies.ToList();
        
        // Processar regeneração do herói
        var heroRegenResult = _regenerationProcessor.ProcessRegeneration(
            state.Hero.ResourceState,
            RegenerationTiming.START_TURN,
            new Dictionary<string, float> { ["turn"] = state.CurrentTurn }
        );
        
        if (heroRegenResult.IsSuccess)
        {
            updatedHero = updatedHero with { ResourceState = heroRegenResult.Value };
        }
        
        // Processar regeneração dos inimigos
        for (int i = 0; i < updatedEnemies.Count; i++)
        {
            var enemy = updatedEnemies[i];
            var enemyRegenResult = _regenerationProcessor.ProcessRegeneration(
                enemy.ResourceState,
                RegenerationTiming.START_TURN,
                new Dictionary<string, float> { ["turn"] = state.CurrentTurn }
            );
            
            if (enemyRegenResult.IsSuccess)
            {
                updatedEnemies[i] = enemy with { ResourceState = enemyRegenResult.Value };
            }
        }
        
        return state with
        {
            Hero = updatedHero,
            Enemies = updatedEnemies
        };
    }
    
    /// <summary>
    /// Processa regeneração de recursos no final do turno
    /// </summary>
    private CombatState ProcessEndOfTurnRegeneration(CombatState state)
    {
        if (_regenerationProcessor == null)
            return state;
        
        _logger.LogDebug("Processing end-of-turn resource regeneration");
        
        var updatedHero = state.Hero;
        var updatedEnemies = state.Enemies.ToList();
        
        // Processar regeneração do herói
        var heroRegenResult = _regenerationProcessor.ProcessRegeneration(
            state.Hero.ResourceState,
            RegenerationTiming.END_TURN,
            new Dictionary<string, float> { ["turn"] = state.CurrentTurn }
        );
        
        if (heroRegenResult.IsSuccess)
        {
            updatedHero = updatedHero with { ResourceState = heroRegenResult.Value };
        }
        
        // Processar regeneração dos inimigos
        for (int i = 0; i < updatedEnemies.Count; i++)
        {
            var enemy = updatedEnemies[i];
            var enemyRegenResult = _regenerationProcessor.ProcessRegeneration(
                enemy.ResourceState,
                RegenerationTiming.END_TURN,
                new Dictionary<string, float> { ["turn"] = state.CurrentTurn }
            );
            
            if (enemyRegenResult.IsSuccess)
            {
                updatedEnemies[i] = enemy with { ResourceState = enemyRegenResult.Value };
            }
        }
        
        return state with
        {
            Hero = updatedHero,
            Enemies = updatedEnemies
        };
    }
}
