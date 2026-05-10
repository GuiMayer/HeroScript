using Core.Combat;
using Core.Common;
using Core.Damage;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.StatusEffects;

namespace Core.Effects;

/// <summary>
/// Implementação do resolvedor de efeitos
/// </summary>
public class EffectResolver : IEffectResolver
{
    private readonly IDamageCalculator _damageCalculator;
    private readonly IResourceManager _resourceManager;
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly IRandomProvider _randomProvider;
    private readonly IStatusEffectManager? _statusEffectManager;
    
    // Cache de modificadores ativos por entidade
    private readonly Dictionary<string, List<EffectModifier>> _activeModifiers = new();
    private readonly object _modifiersLock = new();

    public EffectResolver(
        IDamageCalculator damageCalculator,
        IResourceManager resourceManager,
        IEventBus eventBus,
        ILogger logger,
        IRandomProvider? randomProvider = null,
        IStatusEffectManager? statusEffectManager = null)
    {
        _damageCalculator = damageCalculator;
        _resourceManager = resourceManager;
        _eventBus = eventBus;
        _logger = logger;
        _randomProvider = randomProvider ?? new DefaultRandomProvider();
        _statusEffectManager = statusEffectManager;
    }

    // ===== EXECUÇÃO =====

    public Result<EffectResult> ResolveEffect(EffectInstance effect, CombatState state)
    {
        try
        {
            _logger.LogDebug($"Resolving effect {effect.InstanceId} of type {effect.Definition.Type}");
            
            // 1. Validar se pode executar
            var canExecute = CanExecuteEffect(effect, state);
            if (!canExecute.IsSuccess)
            {
                _logger.LogDebug($"Effect {effect.InstanceId} cannot be executed: {canExecute.Error}");
                return Result<EffectResult>.Failure(canExecute.Error);
            }
            
            // 2. Marcar como executando
            effect = effect.MarkAsExecuting();
            
            // 3. Avaliar condição (se houver)
            if (effect.Definition.Condition != null)
            {
                var conditionMet = EvaluateCondition(effect.Definition.Condition, effect, state);
                if (!conditionMet)
                {
                    _logger.LogDebug($"Effect {effect.InstanceId} condition not met");
                    var failResult = EffectResult.CreateFailure("Condition not met");
                    return Result<EffectResult>.Success(failResult);
                }
            }
            
            // 4. Rolar probabilidade
            if (effect.Definition.Chance < 1.0f)
            {
                var roll = (float)_randomProvider.NextDouble();
                if (roll > effect.Definition.Chance)
                {
                    _logger.LogDebug($"Effect {effect.InstanceId} failed probability check ({roll} > {effect.Definition.Chance})");
                    var failResult = EffectResult.CreateFailure("Probability check failed");
                    return Result<EffectResult>.Success(failResult);
                }
            }
            
            // 5. Resolver alvo(s)
            var targets = ResolveTargets(effect.Definition.Target, effect.SourceEntityId, effect.TargetEntityId, state);
            if (targets.Count == 0)
            {
                _logger.LogWarning($"Effect {effect.InstanceId} has no valid targets");
                return Result<EffectResult>.Success(EffectResult.CreateFailure("No valid targets"));
            }
            
            // 6. Executar effect para cada alvo (com repetições)
            var allResults = new List<EffectResult>();
            var repeat = System.Math.Max(1, effect.Definition.Repeat);
            
            for (int i = 0; i < repeat; i++)
            {
                foreach (var targetId in targets)
                {
                    var result = ExecuteEffectOnTarget(effect, targetId, state);
                    allResults.Add(result);
                }
            }
            
            // 7. Agregar resultados
            var aggregatedResult = AggregateResults(allResults);
            
            // 8. Executar efeitos encadeados
            if (effect.Definition.ChainedEffects != null && aggregatedResult.Success)
            {
                var chainedEffects = new List<EffectInstance>();
                foreach (var chainedDef in effect.Definition.ChainedEffects)
                {
                    var chainedInstance = CreateEffectInstance(chainedDef, effect.SourceEntityId, effect.TargetEntityId);
                    chainedEffects.Add(chainedInstance);
                    
                    // Executar recursivamente
                    var chainedResult = ResolveEffect(chainedInstance, state);
                    if (chainedResult.IsSuccess)
                    {
                        aggregatedResult = aggregatedResult with 
                        { 
                            ChainedEffects = aggregatedResult.ChainedEffects.Concat(new[] { chainedInstance }).ToList()
                        };
                    }
                }
                
                // Publicar evento de chain
                _eventBus.Publish(new EffectChainedEvent
                {
                    ParentEffectId = effect.InstanceId,
                    ChainedEffectIds = chainedEffects.Select(e => e.InstanceId).ToList()
                });
            }
            
            // 9. Publicar evento de execução
            _eventBus.Publish(new EffectExecutedEvent
            {
                EffectInstanceId = effect.InstanceId,
                EffectType = effect.Definition.Type,
                SourceEntityId = effect.SourceEntityId,
                TargetEntityId = effect.TargetEntityId,
                ValueApplied = aggregatedResult.ValueApplied,
                Success = aggregatedResult.Success,
                Metadata = aggregatedResult.Metadata
            });
            
            _logger.LogDebug($"Effect {effect.InstanceId} resolved successfully");
            return Result<EffectResult>.Success(aggregatedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error resolving effect {effect.InstanceId}: {ex.Message}", ex);
            return Result<EffectResult>.Failure($"Error resolving effect: {ex.Message}");
        }
    }

    public Result<List<EffectResult>> ResolveEffects(List<EffectInstance> effects, CombatState state)
    {
        var results = new List<EffectResult>();
        
        foreach (var effect in effects)
        {
            var result = ResolveEffect(effect, state);
            if (!result.IsSuccess)
            {
                _logger.LogWarning($"Effect {effect.InstanceId} failed: {result.Error}");
                // Continuar executando outros effects mesmo se um falhar
            }
            
            if (result.IsSuccess)
            {
                results.Add(result.Value);
            }
        }
        
        return Result<List<EffectResult>>.Success(results);
    }

    // ===== EXECUÇÃO POR TIPO =====

    private EffectResult ExecuteEffectOnTarget(EffectInstance effect, string targetId, CombatState state)
    {
        try
        {
            return effect.Definition.Type switch
            {
                EffectType.DAMAGE => ExecuteDamageEffect(effect, targetId, state),
                EffectType.HEAL => ExecuteHealEffect(effect, targetId, state),
                EffectType.MODIFY_RESOURCE => ExecuteModifyResourceEffect(effect, targetId, state),
                EffectType.GAIN_GOLD => ExecuteGainGoldEffect(effect, targetId, state),
                EffectType.LOSE_GOLD => ExecuteLoseGoldEffect(effect, targetId, state),
                EffectType.APPLY_STATUS => ExecuteApplyStatusEffect(effect, targetId, state),
                EffectType.REMOVE_STATUS => ExecuteRemoveStatusEffect(effect, targetId, state),
                // TODO: Implementar outros tipos conforme necessário
                _ => EffectResult.CreateFailure($"Effect type {effect.Definition.Type} not yet implemented")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error executing effect {effect.InstanceId} on target {targetId}: {ex.Message}", ex);
            return EffectResult.CreateFailure($"Execution error: {ex.Message}");
        }
    }

    private EffectResult ExecuteDamageEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var value = CalculateEffectValue(effect, targetId, state);
        var resourceId = effect.Definition.TargetResource ?? "health";
        
        _logger.LogDebug($"Executing DAMAGE effect: {value} to {resourceId} on {targetId}");
        
        // TODO: Integrar com DamageCalculator para processar através do pipeline
        // Por enquanto, aplicação direta
        
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteHealEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var value = CalculateEffectValue(effect, targetId, state);
        var resourceId = effect.Definition.TargetResource ?? "health";
        
        _logger.LogDebug($"Executing HEAL effect: {value} to {resourceId} on {targetId}");
        
        // TODO: Integrar com HealPipeline quando implementado
        
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteModifyResourceEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var value = CalculateEffectValue(effect, targetId, state);
        var resourceId = effect.Definition.TargetResource ?? "energy";
        
        _logger.LogDebug($"Executing MODIFY_RESOURCE effect: {value} to {resourceId} on {targetId}");
        
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteGainGoldEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var value = CalculateEffectValue(effect, targetId, state);
        
        _logger.LogDebug($"Executing GAIN_GOLD effect: {value} gold to {targetId}");
        
        return EffectResult.CreateSuccess(value, "gold") with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteLoseGoldEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var value = CalculateEffectValue(effect, targetId, state);
        
        _logger.LogDebug($"Executing LOSE_GOLD effect: {value} gold from {targetId}");
        
        return EffectResult.CreateSuccess(-value, "gold") with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteApplyStatusEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var statusId = effect.Definition.StatusId;
        if (string.IsNullOrEmpty(statusId))
        {
            return EffectResult.CreateFailure("StatusId is required for APPLY_STATUS effect");
        }
        
        var stacks = effect.Definition.StatusStacks ?? 1;
        var duration = effect.Definition.StatusDuration;
        
        _logger.LogDebug($"Executing APPLY_STATUS effect: {statusId} ({stacks} stacks) to {targetId}");
        
        // Integrar com StatusEffectManager se disponível
        if (_statusEffectManager != null)
        {
            // Converter string IDs para Guid
            if (!Guid.TryParse(targetId, out var targetGuid))
            {
                return EffectResult.CreateFailure($"Invalid target ID format: {targetId}");
            }
            
            Guid? sourceGuid = null;
            if (!string.IsNullOrEmpty(effect.SourceEntityId) && Guid.TryParse(effect.SourceEntityId, out var parsedSource))
            {
                sourceGuid = parsedSource;
            }
            
            var result = _statusEffectManager.ApplyStatus(targetGuid, statusId, stacks, duration, sourceGuid);
            
            if (!result.IsSuccess)
            {
                return EffectResult.CreateFailure(result.Error ?? "Failed to apply status");
            }
            
            return EffectResult.CreateSuccess() with
            {
                AffectedEntityIds = new List<string> { targetId },
                StatusApplied = new List<string> { statusId }
            };
        }
        
        // Fallback se StatusEffectManager não estiver disponível
        _logger.LogWarning("StatusEffectManager not available, status effect not applied");
        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { targetId },
            StatusApplied = new List<string> { statusId }
        };
    }

    private EffectResult ExecuteRemoveStatusEffect(EffectInstance effect, string targetId, CombatState state)
    {
        var statusId = effect.Definition.StatusId;
        if (string.IsNullOrEmpty(statusId))
        {
            return EffectResult.CreateFailure("StatusId is required for REMOVE_STATUS effect");
        }
        
        _logger.LogDebug($"Executing REMOVE_STATUS effect: {statusId} from {targetId}");
        
        // Integrar com StatusEffectManager se disponível
        if (_statusEffectManager != null)
        {
            // Converter string ID para Guid
            if (!Guid.TryParse(targetId, out var targetGuid))
            {
                return EffectResult.CreateFailure($"Invalid target ID format: {targetId}");
            }
            
            var result = _statusEffectManager.RemoveStatusByStatusId(targetGuid, statusId);
            
            if (!result.IsSuccess)
            {
                return EffectResult.CreateFailure(result.Error ?? "Failed to remove status");
            }
            
            return EffectResult.CreateSuccess() with
            {
                AffectedEntityIds = new List<string> { targetId },
                StatusRemoved = new List<string> { statusId }
            };
        }
        
        // Fallback se StatusEffectManager não estiver disponível
        _logger.LogWarning("StatusEffectManager not available, status effect not removed");
        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { targetId },
            StatusRemoved = new List<string> { statusId }
        };
    }

    // ===== HELPERS =====

    private float CalculateEffectValue(EffectInstance effect, string targetId, CombatState state)
    {
        // Se tem fórmula, avaliar (implementação simplificada por enquanto)
        if (!string.IsNullOrEmpty(effect.Definition.FormulaValue))
        {
            // TODO: Implementar avaliação de fórmulas via MathEngine ou ExpressionEvaluator
            _logger.LogWarning($"Formula evaluation not yet implemented for effect {effect.InstanceId}. Using flat value.");
            return effect.Definition.FlatValue ?? 0f;
        }
        
        // Senão, usar valor flat
        return effect.Definition.FlatValue ?? 0f;
    }

    private bool EvaluateCondition(string condition, EffectInstance effect, CombatState state)
    {
        // TODO: Implementar avaliação de condições via MathEngine ou ExpressionEvaluator
        _logger.LogWarning($"Condition evaluation not yet implemented for effect {effect.InstanceId}. Assuming true.");
        return true;
    }

    private List<string> ResolveTargets(EffectTarget targetType, string sourceId, string primaryTargetId, CombatState state)
    {
        return targetType switch
        {
            EffectTarget.SELF => new List<string> { sourceId },
            EffectTarget.TARGET => new List<string> { primaryTargetId },
            EffectTarget.ALL_ENEMIES => state.Enemies.Select(e => e.EntityId).ToList(),
            EffectTarget.ALL_ALLIES => new List<string> { state.Hero.EntityId }, // TODO: Adicionar aliados quando implementado
            EffectTarget.RANDOM_ENEMY => new List<string> { SelectRandomEnemy(state) },
            EffectTarget.LOWEST_HP_ENEMY => new List<string> { SelectLowestHpEnemy(state) },
            EffectTarget.HIGHEST_HP_ENEMY => new List<string> { SelectHighestHpEnemy(state) },
            _ => new List<string> { primaryTargetId }
        };
    }

    private string SelectRandomEnemy(CombatState state)
    {
        var aliveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (aliveEnemies.Count == 0) return string.Empty;
        
        var index = _randomProvider.Next(0, aliveEnemies.Count);
        return aliveEnemies[index].EntityId;
    }

    private string SelectLowestHpEnemy(CombatState state)
    {
        var aliveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (aliveEnemies.Count == 0) return string.Empty;
        
        return aliveEnemies.OrderBy(e => e.CurrentHp).First().EntityId;
    }

    private string SelectHighestHpEnemy(CombatState state)
    {
        var aliveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (aliveEnemies.Count == 0) return string.Empty;
        
        return aliveEnemies.OrderByDescending(e => e.CurrentHp).First().EntityId;
    }

    private EffectResult AggregateResults(List<EffectResult> results)
    {
        if (results.Count == 0)
        {
            return EffectResult.CreateFailure("No results to aggregate");
        }
        
        if (results.Count == 1)
        {
            return results[0];
        }
        
        // Agregar múltiplos resultados
        var success = results.Any(r => r.Success);
        var totalValue = results.Where(r => r.ValueApplied.HasValue).Sum(r => r.ValueApplied!.Value);
        var affectedEntities = results.SelectMany(r => r.AffectedEntityIds).Distinct().ToList();
        var statusApplied = results.SelectMany(r => r.StatusApplied).Distinct().ToList();
        var statusRemoved = results.SelectMany(r => r.StatusRemoved).Distinct().ToList();
        
        return new EffectResult
        {
            Success = success,
            ValueApplied = totalValue,
            AffectedEntityIds = affectedEntities,
            StatusApplied = statusApplied,
            StatusRemoved = statusRemoved
        };
    }

    // ===== MODIFICAÇÃO =====

    public EffectDefinition ApplyModifiers(EffectDefinition definition, List<EffectModifier> modifiers)
    {
        var modified = definition;
        
        foreach (var modifier in modifiers)
        {
            // Verificar se modificador se aplica
            if (!IsModifierApplicable(modifier, modified))
            {
                continue;
            }
            
            modified = ApplySingleModifier(modified, modifier);
        }
        
        return modified;
    }

    private bool IsModifierApplicable(EffectModifier modifier, EffectDefinition definition)
    {
        // Verificar tags requeridas
        if (modifier.RequiredTags != null && modifier.RequiredTags.Any())
        {
            if (!modifier.RequiredTags.All(tag => definition.Tags.Contains(tag)))
            {
                return false;
            }
        }
        
        // Verificar tags excluídas
        if (modifier.ExcludedTags != null && modifier.ExcludedTags.Any())
        {
            if (modifier.ExcludedTags.Any(tag => definition.Tags.Contains(tag)))
            {
                return false;
            }
        }
        
        // TODO: Avaliar condição do modificador via MathEngine
        
        return true;
    }

    private EffectDefinition ApplySingleModifier(EffectDefinition definition, EffectModifier modifier)
    {
        return modifier.Type switch
        {
            EffectModifierType.MULTIPLY_VALUE => ApplyValueMultiplier(definition, modifier),
            EffectModifierType.ADD_VALUE => ApplyValueAddition(definition, modifier),
            EffectModifierType.CHANGE_TYPE => ApplyTypeChange(definition, modifier),
            EffectModifierType.CHANGE_TARGET => ApplyTargetChange(definition, modifier),
            EffectModifierType.ADD_TAGS => ApplyAddTags(definition, modifier),
            EffectModifierType.REMOVE_TAGS => ApplyRemoveTags(definition, modifier),
            EffectModifierType.MULTIPLY_CHANCE => ApplyChanceMultiplier(definition, modifier),
            EffectModifierType.ADD_REPEAT => ApplyRepeatAddition(definition, modifier),
            _ => definition
        };
    }

    private EffectDefinition ApplyValueMultiplier(EffectDefinition definition, EffectModifier modifier)
    {
        if (!modifier.ValueMultiplier.HasValue) return definition;
        
        if (definition.FlatValue.HasValue)
        {
            return definition with { FlatValue = definition.FlatValue.Value * modifier.ValueMultiplier.Value };
        }
        
        return definition;
    }

    private EffectDefinition ApplyValueAddition(EffectDefinition definition, EffectModifier modifier)
    {
        if (!modifier.ValueAddition.HasValue) return definition;
        
        if (definition.FlatValue.HasValue)
        {
            return definition with { FlatValue = definition.FlatValue.Value + modifier.ValueAddition.Value };
        }
        
        return definition;
    }

    private EffectDefinition ApplyTypeChange(EffectDefinition definition, EffectModifier modifier)
    {
        if (!modifier.OverrideType.HasValue) return definition;
        
        return definition with { Type = modifier.OverrideType.Value };
    }

    private EffectDefinition ApplyTargetChange(EffectDefinition definition, EffectModifier modifier)
    {
        if (!modifier.OverrideTarget.HasValue) return definition;
        
        return definition with { Target = modifier.OverrideTarget.Value };
    }

    private EffectDefinition ApplyAddTags(EffectDefinition definition, EffectModifier modifier)
    {
        if (modifier.AddTags == null || !modifier.AddTags.Any()) return definition;
        
        var newTags = new List<string>(definition.Tags);
        newTags.AddRange(modifier.AddTags.Where(tag => !newTags.Contains(tag)));
        
        return definition with { Tags = newTags };
    }

    private EffectDefinition ApplyRemoveTags(EffectDefinition definition, EffectModifier modifier)
    {
        if (modifier.RemoveTags == null || !modifier.RemoveTags.Any()) return definition;
        
        var newTags = definition.Tags.Where(tag => !modifier.RemoveTags.Contains(tag)).ToList();
        
        return definition with { Tags = newTags };
    }

    private EffectDefinition ApplyChanceMultiplier(EffectDefinition definition, EffectModifier modifier)
    {
        if (!modifier.ChanceMultiplier.HasValue) return definition;
        
        var newChance = System.Math.Min(1.0f, definition.Chance * modifier.ChanceMultiplier.Value);
        return definition with { Chance = newChance };
    }

    private EffectDefinition ApplyRepeatAddition(EffectDefinition definition, EffectModifier modifier)
    {
        if (!modifier.RepeatAddition.HasValue) return definition;
        
        return definition with { Repeat = definition.Repeat + modifier.RepeatAddition.Value };
    }

    public EffectInstance CreateEffectInstance(
        EffectDefinition definition,
        string sourceEntityId,
        string targetEntityId,
        List<EffectModifier>? modifiers = null)
    {
        // Aplicar modificadores se fornecidos
        var finalDefinition = definition;
        if (modifiers != null && modifiers.Any())
        {
            finalDefinition = ApplyModifiers(definition, modifiers);
        }
        
        return new EffectInstance
        {
            Definition = finalDefinition,
            SourceEntityId = sourceEntityId,
            TargetEntityId = targetEntityId,
            AppliedModifiers = modifiers ?? new List<EffectModifier>()
        };
    }

    // ===== VALIDAÇÃO =====

    public Result<bool> CanExecuteEffect(EffectInstance effect, CombatState state)
    {
        // Verificar se entidades existem
        var source = state.GetEntity(effect.SourceEntityId);
        if (source == null)
        {
            return Result<bool>.Failure($"Source entity {effect.SourceEntityId} not found");
        }
        
        var target = state.GetEntity(effect.TargetEntityId);
        if (target == null && effect.Definition.Target == EffectTarget.TARGET)
        {
            return Result<bool>.Failure($"Target entity {effect.TargetEntityId} not found");
        }
        
        // Verificar se alvo está vivo (para efeitos que requerem alvo vivo)
        if (target != null && !target.IsAlive && RequiresLiveTarget(effect.Definition.Type))
        {
            return Result<bool>.Failure($"Target entity {effect.TargetEntityId} is not alive");
        }
        
        return Result<bool>.Success(true);
    }

    private bool RequiresLiveTarget(EffectType type)
    {
        return type switch
        {
            EffectType.DAMAGE => true,
            EffectType.HEAL => true,
            EffectType.APPLY_STATUS => true,
            _ => false
        };
    }

    public Result<bool> ValidateDefinition(EffectDefinition definition)
    {
        // Validar campos obrigatórios por tipo
        switch (definition.Type)
        {
            case EffectType.DAMAGE:
            case EffectType.HEAL:
            case EffectType.MODIFY_RESOURCE:
                if (definition.FlatValue == null && string.IsNullOrEmpty(definition.FormulaValue))
                {
                    return Result<bool>.Failure($"Effect type {definition.Type} requires FlatValue or FormulaValue");
                }
                break;
                
            case EffectType.APPLY_STATUS:
            case EffectType.REMOVE_STATUS:
                if (string.IsNullOrEmpty(definition.StatusId))
                {
                    return Result<bool>.Failure($"Effect type {definition.Type} requires StatusId");
                }
                break;
        }
        
        // Validar chance
        if (definition.Chance < 0f || definition.Chance > 1.0f)
        {
            return Result<bool>.Failure($"Chance must be between 0.0 and 1.0, got {definition.Chance}");
        }
        
        // Validar repeat
        if (definition.Repeat < 1)
        {
            return Result<bool>.Failure($"Repeat must be at least 1, got {definition.Repeat}");
        }
        
        return Result<bool>.Success(true);
    }

    // ===== QUERY =====

    public List<EffectModifier> GetActiveModifiers(string entityId, EffectType? filterType = null)
    {
        lock (_modifiersLock)
        {
            if (!_activeModifiers.TryGetValue(entityId, out var modifiers))
            {
                return new List<EffectModifier>();
            }
            
            if (filterType.HasValue)
            {
                // TODO: Filtrar por tipo quando implementarmos metadata de tipo no modifier
                return modifiers;
            }
            
            return new List<EffectModifier>(modifiers);
        }
    }

    // ===== GERENCIAMENTO DE MODIFICADORES =====

    public void RegisterModifier(string entityId, EffectModifier modifier)
    {
        lock (_modifiersLock)
        {
            if (!_activeModifiers.ContainsKey(entityId))
            {
                _activeModifiers[entityId] = new List<EffectModifier>();
            }
            
            _activeModifiers[entityId].Add(modifier);
            _logger.LogDebug($"Registered modifier {modifier.ModifierId} for entity {entityId}");
        }
    }

    public void UnregisterModifier(string entityId, string modifierId)
    {
        lock (_modifiersLock)
        {
            if (_activeModifiers.TryGetValue(entityId, out var modifiers))
            {
                modifiers.RemoveAll(m => m.ModifierId == modifierId);
                _logger.LogDebug($"Unregistered modifier {modifierId} for entity {entityId}");
            }
        }
    }
}
