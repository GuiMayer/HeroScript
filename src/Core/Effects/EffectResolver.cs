using Core.Combat.Models;
using Core.Common;
using Core.Damage;
using Core.Events;
using Core.Events.Domain;
using Core.Logging;
using Core.Math;
using Core.Resources;
using Core.Run;
using Core.StatusEffects;
using System.Text.Json;

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
    private readonly IRunManager? _runManager;
    private readonly IRuntimeFormulaEvaluator _formulaEvaluator;
    
    // Cache de modificadores ativos por entidade
    private readonly Dictionary<string, List<EffectModifier>> _activeModifiers = new();
    private readonly object _modifiersLock = new();

    public EffectResolver(
        IDamageCalculator damageCalculator,
        IResourceManager resourceManager,
        IEventBus eventBus,
        ILogger logger,
        IRuntimeFormulaEvaluator formulaEvaluator,
        IRandomProvider? randomProvider = null,
        IStatusEffectManager? statusEffectManager = null,
        IRunManager? runManager = null)
    {
        _damageCalculator = damageCalculator;
        _resourceManager = resourceManager;
        _eventBus = eventBus;
        _logger = logger;
        _formulaEvaluator = formulaEvaluator ?? throw new ArgumentNullException(nameof(formulaEvaluator));
        _randomProvider = randomProvider ?? new DefaultRandomProvider();
        _statusEffectManager = statusEffectManager;
        _runManager = runManager;
    }

    // ===== EXECUÇÃO =====

    public Result<EffectResult> ResolveEffect(EffectInstance effect, CombatState state)
    {
        var result = ApplyEffect(effect, CombatEffectContext.FromEffect(effect, state));
        if (result.IsFailure)
            return Result<EffectResult>.Failure(result.Error);

        return Result<EffectResult>.Success(result.Value.EffectResult);
    }

    public Result<EffectApplicationResult> ApplyEffect(EffectInstance effect, IEffectContext context)
        => ApplyEffectCore(effect, context, _randomProvider, useExplicitDamageRandom: false);

    public Result<EffectApplicationResult> ApplyEffect(
        EffectInstance effect,
        IEffectContext context,
        IRandomProvider randomProvider)
        => ApplyEffectCore(effect, context, randomProvider, useExplicitDamageRandom: true);

    private Result<EffectApplicationResult> ApplyEffectCore(
        EffectInstance effect,
        IEffectContext context,
        IRandomProvider randomProvider,
        bool useExplicitDamageRandom)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(randomProvider);
            effect = MaterializeEffect(effect, randomProvider, "effect");
            _logger.LogDebug($"Resolving effect {effect.InstanceId} of type {effect.Definition.Type}");
            
            // 1. Validar se pode executar
            var canExecute = CanExecuteEffect(effect, context);
            if (!canExecute.IsSuccess)
            {
                _logger.LogDebug($"Effect {effect.InstanceId} cannot be executed: {canExecute.Error}");
                return Result<EffectApplicationResult>.Failure(canExecute.Error);
            }
            
            // 2. Marcar como executando
            effect = effect.MarkAsExecuting();
            
            // 3. Avaliar condição (se houver)
            if (effect.Definition.Condition != null)
            {
                var conditionMet = EvaluateCondition(effect.Definition.Condition, effect, context);
                if (!conditionMet)
                {
                    _logger.LogDebug($"Effect {effect.InstanceId} condition not met");
                    var failResult = EffectResult.CreateFailure("Condition not met");
                    return Result<EffectApplicationResult>.Success(EffectApplicationResult.FromEffectResult(context.Scope, failResult, context.CombatState));
                }
            }
            
            // 4. Rolar probabilidade
            if (effect.Definition.Chance < 1.0f)
            {
                var roll = (float)randomProvider.NextDouble();
                if (roll > effect.Definition.Chance)
                {
                    _logger.LogDebug($"Effect {effect.InstanceId} failed probability check ({roll} > {effect.Definition.Chance})");
                    var failResult = EffectResult.CreateFailure("Probability check failed");
                    return Result<EffectApplicationResult>.Success(EffectApplicationResult.FromEffectResult(context.Scope, failResult, context.CombatState));
                }
            }
            
            // 5. Resolver alvo(s)
            var targets = ResolveTargets(
                effect.Definition.Target,
                effect.SourceEntityId,
                effect.TargetEntityId,
                context,
                randomProvider);
            if (targets.Count == 0)
            {
                _logger.LogWarning($"Effect {effect.InstanceId} has no valid targets");
                return Result<EffectApplicationResult>.Success(EffectApplicationResult.FromEffectResult(context.Scope, EffectResult.CreateFailure("No valid targets"), context.CombatState));
            }
            
            // 6. Executar effect para cada alvo (com repetições)
            var allResults = new List<EffectResult>();
            var repeat = System.Math.Max(1, effect.Definition.Repeat);
            
            for (int i = 0; i < repeat; i++)
            {
                foreach (var targetId in targets)
                {
                    var result = ExecuteEffectOnTarget(
                        effect,
                        targetId,
                        context,
                        randomProvider,
                        useExplicitDamageRandom);
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
                    var chainedResult = ApplyEffectCore(
                        chainedInstance,
                        context,
                        randomProvider,
                        useExplicitDamageRandom);
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
            return Result<EffectApplicationResult>.Success(EffectApplicationResult.FromEffectResult(context.Scope, aggregatedResult, context.CombatState));
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error resolving effect {effect.InstanceId}: {ex.Message}", ex);
            return Result<EffectApplicationResult>.Failure($"Error resolving effect: {ex.Message}");
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

    public Result<List<EffectApplicationResult>> ApplyEffects(List<EffectInstance> effects, IEffectContext context)
    {
        var results = new List<EffectApplicationResult>();
        
        foreach (var effect in effects)
        {
            var result = ApplyEffect(effect, context);
            if (!result.IsSuccess)
            {
                _logger.LogWarning($"Effect {effect.InstanceId} failed: {result.Error}");
            }
            
            if (result.IsSuccess)
            {
                results.Add(result.Value);
            }
        }
        
        return Result<List<EffectApplicationResult>>.Success(results);
    }

    // ===== EXECUÇÃO POR TIPO =====

    private EffectResult ExecuteEffectOnTarget(
        EffectInstance effect,
        string targetId,
        IEffectContext context,
        IRandomProvider randomProvider,
        bool useExplicitDamageRandom)
    {
        try
        {
            return effect.Definition.Type switch
            {
                // Combat
                EffectType.DAMAGE => ExecuteDamageEffect(
                    effect,
                    targetId,
                    context,
                    randomProvider,
                    useExplicitDamageRandom),
                EffectType.HEAL => ExecuteHealEffect(effect, targetId, context),
                EffectType.MODIFY_RESOURCE => ExecuteModifyResourceEffect(effect, targetId, context),

                // Economy
                EffectType.GAIN_GOLD => ExecuteGainGoldEffect(effect, targetId, context),
                EffectType.LOSE_GOLD => ExecuteLoseGoldEffect(effect, targetId, context),
                EffectType.GAIN_PP => ExecuteEconomyEffect(effect, targetId, "pp", 1f, context),
                EffectType.LOSE_PP => ExecuteEconomyEffect(effect, targetId, "pp", -1f, context),

                // Status
                EffectType.APPLY_STATUS => ExecuteApplyStatusEffect(effect, targetId, context),
                EffectType.REMOVE_STATUS => ExecuteRemoveStatusEffect(effect, targetId, context),
                EffectType.DISPEL_STATUS => ExecuteDispelStatusEffect(effect, targetId, context),

                // Deck
                EffectType.DRAW_CARD => ExecuteDeckEffect(effect, targetId, "DRAW_CARD", context),
                EffectType.DISCARD_CARD => ExecuteDeckEffect(effect, targetId, "DISCARD_CARD", context),
                EffectType.EXHAUST_CARD => ExecuteDeckEffect(effect, targetId, "EXHAUST_CARD", context),
                EffectType.ADD_CARD_TO_HAND => ExecuteDeckEffect(effect, targetId, "ADD_CARD_TO_HAND", context),

                // Modifiers (metadata-only, applied by caller/pipeline)
                EffectType.MODIFY_DAMAGE_DEALT => ExecuteModifierEffect(effect, targetId, "damage_dealt"),
                EffectType.MODIFY_DAMAGE_TAKEN => ExecuteModifierEffect(effect, targetId, "damage_taken"),
                EffectType.MODIFY_CRIT_CHANCE => ExecuteModifierEffect(effect, targetId, "crit_chance"),
                EffectType.MODIFY_CRIT_MULT => ExecuteModifierEffect(effect, targetId, "crit_mult"),
                EffectType.MODIFY_COOLDOWNS => ExecuteModifierEffect(effect, targetId, "cooldowns"),

                // Combat control (require status/combat state in future)
                EffectType.PREVENT_ACTIONS or
                EffectType.FORCE_TARGET or
                EffectType.SKIP_TURN or
                EffectType.REFLECT_DAMAGE or
                EffectType.ABSORB_DAMAGE => ExecuteControlEffect(effect, targetId, context),

                _ => EffectResult.CreateFailure($"Effect type {effect.Definition.Type} not yet implemented")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error executing effect {effect.InstanceId} on target {targetId}: {ex.Message}", ex);
            return EffectResult.CreateFailure($"Execution error: {ex.Message}");
        }
    }

    private EffectResult ExecuteDamageEffect(
        EffectInstance effect,
        string targetId,
        IEffectContext context,
        IRandomProvider randomProvider,
        bool useExplicitDamageRandom)
    {
        if (context.CombatState == null)
            return EffectResult.CreateFailure("DAMAGE effect requires combat context");

        var value = CalculateEffectValue(effect, targetId, context);
        var resourceId = effect.Definition.TargetResource ?? "health";
        
        _logger.LogDebug($"Executing DAMAGE effect: {value} to {resourceId} on {targetId}");

        var source = context.CombatState.GetEntity(effect.SourceEntityId);
        var target = context.CombatState.GetEntity(targetId);
        if (source != null && target != null && resourceId == "health")
        {
            var action = new ActionDefinition
            {
                ActionId = effect.SourceActionId ?? effect.Definition.EffectId,
                Effects = new List<EffectDefinition> { effect.Definition with { FlatValue = value, FormulaValue = null } },
                Tags = effect.Definition.Tags.ToList()
            };

            var damageResult = useExplicitDamageRandom
                ? _damageCalculator.CalculateDamage(action, source, target, randomProvider)
                : _damageCalculator.CalculateDamage(action, source, target);
            value = damageResult.FinalDamage;
        }
        
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteHealEffect(EffectInstance effect, string targetId, IEffectContext context)
    {
        var value = CalculateEffectValue(effect, targetId, context);
        var resourceId = effect.Definition.TargetResource ?? "health";
        
        _logger.LogDebug($"Executing HEAL effect: {value} to {resourceId} on {targetId}");
        
        // TODO: Integrar com HealPipeline quando implementado
        
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteModifyResourceEffect(EffectInstance effect, string targetId, IEffectContext context)
    {
        var value = CalculateEffectValue(effect, targetId, context);
        var resourceId = effect.Definition.TargetResource ?? "energy";
        
        _logger.LogDebug($"Executing MODIFY_RESOURCE effect: {value} to {resourceId} on {targetId}");
        
        return EffectResult.CreateSuccess(value, resourceId) with
        {
            AffectedEntityIds = new List<string> { targetId }
        };
    }

    private EffectResult ExecuteGainGoldEffect(EffectInstance effect, string targetId, IEffectContext context)
    {
        var value = CalculateEffectValue(effect, targetId, context);
        
        _logger.LogDebug($"Executing GAIN_GOLD effect: {value} gold to {targetId}");

        var runApply = ApplyRunEconomy(context, "gold", (int)System.MathF.Round(value));
        if (runApply.IsFailure)
            return EffectResult.CreateFailure(runApply.Error);
        
        return EffectResult.CreateSuccess(value, "gold") with
        {
            AffectedEntityIds = new List<string> { targetId },
            Metadata = RunStateMetadata(runApply.Value)
        };
    }

    private EffectResult ExecuteLoseGoldEffect(EffectInstance effect, string targetId, IEffectContext context)
    {
        var value = CalculateEffectValue(effect, targetId, context);
        
        _logger.LogDebug($"Executing LOSE_GOLD effect: {value} gold from {targetId}");

        var runApply = ApplyRunEconomy(context, "gold", -(int)System.MathF.Round(value));
        if (runApply.IsFailure)
            return EffectResult.CreateFailure(runApply.Error);
        
        return EffectResult.CreateSuccess(-value, "gold") with
        {
            AffectedEntityIds = new List<string> { targetId },
            Metadata = RunStateMetadata(runApply.Value)
        };
    }

    private EffectResult ExecuteApplyStatusEffect(EffectInstance effect, string targetId, IEffectContext context)
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

    private EffectResult ExecuteRemoveStatusEffect(EffectInstance effect, string targetId, IEffectContext context)
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

    private EffectResult ExecuteDispelStatusEffect(EffectInstance effect, string targetId, IEffectContext context)
    {
        _logger.LogDebug($"Executing DISPEL_STATUS effect on {targetId}");

        if (_statusEffectManager != null)
        {
            if (!Guid.TryParse(targetId, out var targetGuid))
            {
                return EffectResult.CreateFailure($"Invalid target ID format: {targetId}");
            }

            var result = _statusEffectManager.RemoveAllStatus(targetGuid);

            if (!result.IsSuccess)
            {
                return EffectResult.CreateFailure(result.Error ?? "Failed to dispel status effects");
            }

            return EffectResult.CreateSuccess() with
            {
                AffectedEntityIds = new List<string> { targetId },
                StatusRemoved = new List<string> { "*" }
            };
        }

        _logger.LogWarning("StatusEffectManager not available, dispel not applied");
        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { targetId },
            StatusRemoved = new List<string> { "*" }
        };
    }

    private EffectResult ExecuteEconomyEffect(EffectInstance effect, string targetId, string resource, float sign, IEffectContext context)
    {
        var value = (effect.Definition.FlatValue ?? 0f) * System.Math.Abs(sign);
        var signed = sign < 0 ? -value : value;

        _logger.LogDebug($"Executing economy effect: {resource} {signed:+0;-#} for {targetId}");

        var runApply = ApplyRunEconomy(context, resource, (int)System.MathF.Round(signed));
        if (runApply.IsFailure)
            return EffectResult.CreateFailure(runApply.Error);

        return EffectResult.CreateSuccess() with
        {
            ValueApplied = signed,
            ResourceAffected = resource,
            AffectedEntityIds = new List<string> { targetId },
            Metadata = RunStateMetadata(runApply.Value, new Dictionary<string, object> { ["economyResource"] = resource })
        };
    }

    private EffectResult ExecuteDeckEffect(EffectInstance effect, string targetId, string operation, IEffectContext context)
    {
        var count = (int)(effect.Definition.FlatValue ?? 1f);

        _logger.LogDebug($"Executing deck effect: {operation} x{count} for {targetId}");

        var deckApply = ApplyRunDeckOperation(context, effect, operation, count);
        if (deckApply.IsFailure)
            return EffectResult.CreateFailure(deckApply.Error);

        return EffectResult.CreateSuccess() with
        {
            ValueApplied = count,
            AffectedEntityIds = new List<string> { targetId },
            Metadata = RunStateMetadata(deckApply.Value.RunState, new Dictionary<string, object>
            {
                ["deckOperation"] = operation,
                ["count"] = count,
                ["cards"] = deckApply.Value.Cards
            })
        };
    }

    private Result<RunState?> ApplyRunEconomy(IEffectContext context, string resource, int amount)
    {
        var runContext = context as RunEffectContext;
        if (runContext?.RunState == null)
            return Result<RunState?>.Success(null);

        if (_runManager == null)
            return Result<RunState?>.Failure("Run economy effects require IRunManager");

        return _runManager.ApplyEconomy(runContext.RunState.RunId, resource, amount)
            .Map<RunState?>(state => state);
    }

    private Result<(RunState? RunState, IReadOnlyList<string> Cards)> ApplyRunDeckOperation(
        IEffectContext context,
        EffectInstance effect,
        string operation,
        int count)
    {
        var runContext = context as RunEffectContext;
        if (runContext?.RunState == null)
            return Result<(RunState? RunState, IReadOnlyList<string> Cards)>.Success((null, Array.Empty<string>()));

        if (_runManager == null)
            return Result<(RunState? RunState, IReadOnlyList<string> Cards)>.Failure("Run deck effects require IRunManager");

        Result<IReadOnlyList<string>> operationResult = operation switch
        {
            "DRAW_CARD" => _runManager.DrawCards(runContext.RunState.RunId, count),
            "DISCARD_CARD" => _runManager.DiscardCards(runContext.RunState.RunId, SelectCards(effect, runContext.RunState.Deck.Hand, count)),
            "EXHAUST_CARD" => _runManager.ExhaustCards(runContext.RunState.RunId, SelectCards(effect, runContext.RunState.Deck.Hand, count)),
            "ADD_CARD_TO_HAND" => _runManager.AddCardsToHand(runContext.RunState.RunId, ResolveCardIds(effect, count)),
            _ => Result<IReadOnlyList<string>>.Failure($"Unsupported deck operation: {operation}")
        };

        if (operationResult.IsFailure)
            return Result<(RunState? RunState, IReadOnlyList<string> Cards)>.Failure(operationResult.Error);

        var updatedRun = _runManager.GetRun(runContext.RunState.RunId);
        if (updatedRun.IsFailure)
            return Result<(RunState? RunState, IReadOnlyList<string> Cards)>.Failure(updatedRun.Error);

        return Result<(RunState? RunState, IReadOnlyList<string> Cards)>.Success((updatedRun.Value, operationResult.Value));
    }

    private static IReadOnlyList<string> SelectCards(EffectInstance effect, IReadOnlyList<string> source, int count)
    {
        var explicitCards = ResolveCardIds(effect, count);
        return explicitCards.Count > 0 ? explicitCards : source.Take(count).ToList();
    }

    private static IReadOnlyList<string> ResolveCardIds(EffectInstance effect, int count)
    {
        if (effect.Definition.Metadata.TryGetValue("cardIds", out var cardIds))
        {
            if (cardIds is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                return element.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToList();
            }

            if (cardIds is IEnumerable<string> strings)
                return strings.ToList();
        }

        if (effect.Definition.Metadata.TryGetValue("cardId", out var cardId))
        {
            if (cardId is JsonElement element && element.ValueKind == JsonValueKind.String)
                return Enumerable.Repeat(element.GetString()!, count).ToList();

            if (cardId is string value && !string.IsNullOrWhiteSpace(value))
                return Enumerable.Repeat(value, count).ToList();
        }

        return Array.Empty<string>();
    }

    private static Dictionary<string, object> RunStateMetadata(RunState? state, Dictionary<string, object>? metadata = null)
    {
        var result = metadata != null
            ? new Dictionary<string, object>(metadata)
            : new Dictionary<string, object>();

        result["stateApplied"] = state != null;
        if (state != null)
        {
            result["runId"] = state.RunId;
            result["gold"] = state.Gold;
            result["powerPoints"] = state.PowerPoints;
            result["handCount"] = state.Deck.Hand.Count;
            result["drawPileCount"] = state.Deck.DrawPile.Count;
            result["discardPileCount"] = state.Deck.DiscardPile.Count;
            result["exhaustPileCount"] = state.Deck.ExhaustPile.Count;
        }

        return result;
    }

    private EffectResult ExecuteModifierEffect(EffectInstance effect, string targetId, string modifierKey)
    {
        var value = effect.Definition.FlatValue ?? effect.Definition.ModifierValue ?? 0f;

        _logger.LogDebug($"Executing modifier effect: {modifierKey} = {value} for {targetId}");

        return EffectResult.CreateSuccess() with
        {
            ValueApplied = value,
            ResourceAffected = modifierKey,
            AffectedEntityIds = new List<string> { targetId },
            Metadata = new Dictionary<string, object>
            {
                ["modifierKey"] = modifierKey,
                ["modifierValue"] = value,
                ["stateApplied"] = false
            }
        };
    }

    private EffectResult ExecuteControlEffect(EffectInstance effect, string targetId, IEffectContext context)
    {
        if (context.CombatState == null)
            return EffectResult.CreateFailure($"{effect.Definition.Type} effect requires combat context");

        var typeName = effect.Definition.Type.ToString();

        _logger.LogDebug($"Executing control effect: {typeName} on {targetId}");

        // Control effects are applied as status/flags by CombatSystem;
        // here we signal intent and metadata for the caller to act on.
        return EffectResult.CreateSuccess() with
        {
            AffectedEntityIds = new List<string> { targetId },
            Metadata = new Dictionary<string, object>
            {
                ["controlEffect"] = typeName,
                ["stateApplied"] = false
            }
        };
    }

    // ===== HELPERS =====

    private float CalculateEffectValue(EffectInstance effect, string targetId, IEffectContext context)
    {
        if (!string.IsNullOrEmpty(effect.Definition.FormulaValue))
        {
            var variables = BuildFormulaVariables(effect, targetId, context);
            var formulaValue = _formulaEvaluator.Evaluate(effect.Definition.FormulaValue, variables);
            if (formulaValue.IsSuccess)
                return formulaValue.Value;

            _logger.LogWarning($"Failed to evaluate formula for effect {effect.InstanceId}: {formulaValue.Error}. Using flat value.");
        }
        
        return effect.Definition.FlatValue ?? 0f;
    }

    private Dictionary<string, float> BuildFormulaVariables(EffectInstance effect, string targetId, IEffectContext context)
    {
        var variables = new Dictionary<string, float>();
        var source = context.CombatState?.GetEntity(effect.SourceEntityId);
        var target = context.CombatState?.GetEntity(targetId);

        if (source != null)
        {
            variables["source_hp"] = source.GetResource("health")?.Current ?? 0f;
            variables["source_max_hp"] = source.GetResource("health")?.Maximum ?? 0f;
        }

        if (target != null)
        {
            variables["target_hp"] = target.GetResource("health")?.Current ?? 0f;
            variables["target_max_hp"] = target.GetResource("health")?.Maximum ?? 0f;
        }

        return variables;
    }

    private bool EvaluateCondition(string condition, EffectInstance effect, IEffectContext context)
    {
        var targetId = string.IsNullOrWhiteSpace(effect.TargetEntityId)
            ? effect.SourceEntityId
            : effect.TargetEntityId;
        var variables = BuildFormulaVariables(effect, targetId, context);
        var result = _formulaEvaluator.Evaluate(condition, variables);
        if (result.IsFailure)
        {
            _logger.LogWarning($"Failed to evaluate condition for effect {effect.InstanceId}: {result.Error}");
            return false;
        }

        return result.Value > 0f;
    }

    private List<string> ResolveTargets(
        EffectTarget targetType,
        string sourceId,
        string primaryTargetId,
        IEffectContext context,
        IRandomProvider randomProvider)
    {
        if (context.CombatState == null)
        {
            return targetType switch
            {
                EffectTarget.SELF => new List<string> { sourceId },
                EffectTarget.TARGET => new List<string> { primaryTargetId },
                _ => new List<string>()
            };
        }

        return targetType switch
        {
            EffectTarget.SELF => new List<string> { sourceId },
            EffectTarget.TARGET => new List<string> { primaryTargetId },
            EffectTarget.ALL_ENEMIES => context.CombatState.Enemies.Select(e => e.EntityId).ToList(),
            EffectTarget.ALL_ALLIES => new List<string> { context.CombatState.Hero.EntityId }, // TODO: Adicionar aliados quando implementado
            EffectTarget.RANDOM_ENEMY => new List<string> { SelectRandomEnemy(context.CombatState, randomProvider) },
            EffectTarget.LOWEST_HP_ENEMY => new List<string> { SelectLowestHpEnemy(context.CombatState) },
            EffectTarget.HIGHEST_HP_ENEMY => new List<string> { SelectHighestHpEnemy(context.CombatState) },
            _ => new List<string> { primaryTargetId }
        };
    }

    private static string SelectRandomEnemy(CombatState state, IRandomProvider randomProvider)
    {
        var aliveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (aliveEnemies.Count == 0) return string.Empty;
        
        var index = randomProvider.Next(0, aliveEnemies.Count);
        return aliveEnemies[index].EntityId;
    }

    private static EffectInstance MaterializeEffect(
        EffectInstance effect,
        IRandomProvider randomProvider,
        string scope)
    {
        if (randomProvider is not DeterministicRandomProvider deterministic)
            return effect;

        var instanceId = string.IsNullOrWhiteSpace(effect.InstanceId)
            ? deterministic.AllocateId(scope).ToString("D")
            : effect.InstanceId;
        var createdAt = effect.CreatedAt == DateTime.UnixEpoch
            ? deterministic.LogicalTimestamp
            : effect.CreatedAt;
        return effect with { InstanceId = instanceId, CreatedAt = createdAt };
    }

    private string SelectLowestHpEnemy(CombatState state)
    {
        var aliveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (aliveEnemies.Count == 0) return string.Empty;
        
        return aliveEnemies.OrderBy(e => e.GetResource("health")?.Current ?? 0f).First().EntityId;
    }

    private string SelectHighestHpEnemy(CombatState state)
    {
        var aliveEnemies = state.Enemies.Where(e => e.IsAlive).ToList();
        if (aliveEnemies.Count == 0) return string.Empty;
        
        return aliveEnemies.OrderByDescending(e => e.GetResource("health")?.Current ?? 0f).First().EntityId;
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
        return CanExecuteEffect(effect, CombatEffectContext.FromEffect(effect, state));
    }

    public Result<bool> CanExecuteEffect(EffectInstance effect, IEffectContext context)
    {
        if (context.CombatState == null)
        {
            return RequiresCombatContext(effect.Definition.Type)
                ? Result<bool>.Failure($"Effect type {effect.Definition.Type} requires combat context")
                : Result<bool>.Success(true);
        }

        // Verificar se entidades existem
        var source = context.CombatState.GetEntity(effect.SourceEntityId);
        if (source == null)
        {
            return Result<bool>.Failure($"Source entity {effect.SourceEntityId} not found");
        }
        
        var target = context.CombatState.GetEntity(effect.TargetEntityId);
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

    private static bool RequiresCombatContext(EffectType type)
    {
        return type switch
        {
            EffectType.DAMAGE => true,
            EffectType.HEAL => true,
            EffectType.APPLY_STATUS => true,
            EffectType.REMOVE_STATUS => true,
            EffectType.DISPEL_STATUS => true,
            EffectType.PREVENT_ACTIONS => true,
            EffectType.FORCE_TARGET => true,
            EffectType.SKIP_TURN => true,
            EffectType.REFLECT_DAMAGE => true,
            EffectType.ABSORB_DAMAGE => true,
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
