using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Common;
using Core.Events;
using Core.Logging;
using Core.Math;

namespace Core.Damage;

/// <summary>
/// Processador genérico de bucket que executa operações baseadas em definição JSON.
/// Cada bucket é uma instância deste processador com uma definição diferente.
/// </summary>
public class GenericBucketProcessor
{
    private readonly BucketDefinition _definition;
    private readonly IMathEngine _mathEngine;
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private readonly IRandomProvider _randomProvider;

    public GenericBucketProcessor(
        BucketDefinition definition,
        IMathEngine mathEngine,
        IEventBus eventBus,
        ILogger logger,
        IRandomProvider? randomProvider = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _randomProvider = randomProvider ?? new DefaultRandomProvider();
    }

    /// <summary>
    /// Processa o contexto de dano através deste bucket
    /// </summary>
    public DamageContext Process(DamageContext context)
    {
        // 1. Avaliar filtros
        if (!EvaluateFilters(context))
        {
            _logger.LogDebug($"Bucket '{_definition.BucketId}' skipped (filters failed)");
            return context;
        }

        var inputDamage = context.CurrentDamage;
        var workingContext = context;

        // 2. Executar operações sequencialmente
        foreach (var operation in _definition.Operations)
        {
            workingContext = ExecuteOperation(workingContext, operation);
        }

        // 3. Emitir evento se configurado
        if (_definition.EmitEvents)
        {
            EmitBucketProcessedEvent(inputDamage, workingContext);
        }

        _logger.LogDebug($"Bucket '{_definition.BucketId}': {inputDamage:F2} → {workingContext.CurrentDamage:F2}");

        return workingContext;
    }

    private bool EvaluateFilters(DamageContext context)
    {
        foreach (var filter in _definition.FilterConditions)
        {
            if (!EvaluateFilter(context, filter))
                return false;
        }
        return true;
    }

    private bool EvaluateFilter(DamageContext context, FilterCondition filter)
    {
        switch (filter.Type)
        {
            case FilterType.TAG_PRESENT:
                return context.Tags.Contains(filter.Parameter);

            case FilterType.TAG_ABSENT:
                return !context.Tags.Contains(filter.Parameter);

            case FilterType.MODIFIER_PRESENT:
                return context.Modifiers.ContainsKey(filter.Parameter);

            case FilterType.MODIFIER_ABOVE:
                if (filter.Value == null) return false;
                return context.Modifiers.TryGetValue(filter.Parameter, out var val)
                    && val > Convert.ToSingle(filter.Value);

            case FilterType.MODIFIER_BELOW:
                if (filter.Value == null) return false;
                return context.Modifiers.TryGetValue(filter.Parameter, out var val2)
                    && val2 < Convert.ToSingle(filter.Value);

            default:
                _logger.LogWarning($"Unknown filter type: {filter.Type}");
                return true;
        }
    }

    private DamageContext ExecuteOperation(DamageContext context, BucketOperation op)
    {
        try
        {
            return op.Type switch
            {
                OperationType.ADD_FLAT => ExecuteAddFlat(context, op),
                OperationType.MULTIPLY => ExecuteMultiply(context, op),
                OperationType.APPLY_FORMULA => ExecuteApplyFormula(context, op),
                OperationType.ROLL_CRIT_TIER => ExecuteRollCritTier(context, op),
                OperationType.SET_TAG => ExecuteSetTag(context, op),
                OperationType.REMOVE_TAG => ExecuteRemoveTag(context, op),
                OperationType.SET_MODIFIER => ExecuteSetModifier(context, op),
                OperationType.ADD_TO_MODIFIER => ExecuteAddToModifier(context, op),
                OperationType.ADD_MORE_MULTIPLIER => ExecuteAddMoreMultiplier(context, op),
                OperationType.APPLY_MORE_MULTIPLIERS => ExecuteApplyMoreMultipliers(context, op),
                _ => throw new InvalidOperationException($"Unknown operation type: {op.Type}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error executing operation {op.Type} in bucket {_definition.BucketId}: {ex.Message}", ex);
            throw new InvalidOperationException(
                $"Failed to execute operation {op.Type} in bucket {_definition.BucketId}: {ex.Message}", 
                ex);
        }
    }

    private DamageContext ExecuteSetTag(DamageContext context, BucketOperation op)
    {
        // Remove "tag:" prefix if present
        var tagName = op.Source.StartsWith("tag:") ? op.Source.Replace("tag:", "") : op.Source;
        return context.WithTag(tagName);
    }

    private DamageContext ExecuteRemoveTag(DamageContext context, BucketOperation op)
    {
        // Remove "tag:" prefix if present
        var tagName = op.Source.StartsWith("tag:") ? op.Source.Replace("tag:", "") : op.Source;
        return context.RemoveTag(tagName);
    }

    private DamageContext ExecuteAddFlat(DamageContext context, BucketOperation op)
    {
        var result = ResolveValueSafe(context, op.Source);
        if (result.IsFailure)
        {
            _logger.LogError($"[{_definition.BucketId}] Failed to resolve value in ADD_FLAT: {result.Error}");
            return context;
        }
        return context.WithDamage(context.CurrentDamage + result.Value);
    }

    private DamageContext ExecuteMultiply(DamageContext context, BucketOperation op)
    {
        var result = ResolveValueSafe(context, op.Source);
        if (result.IsFailure)
        {
            _logger.LogError($"[{_definition.BucketId}] Failed to resolve multiplier in MULTIPLY: {result.Error}");
            return context;
        }
        return context.WithDamage(context.CurrentDamage * result.Value);
    }

    private DamageContext ExecuteApplyFormula(DamageContext context, BucketOperation op)
    {
        // Source format: "formula:FORMULA_NAME"
        var formulaName = op.Source.Replace("formula:", "");

        // Construir parâmetros da fórmula a partir de op.Parameters
        var formulaParams = new Dictionary<string, float>();
        foreach (var param in op.Parameters)
        {
            if (param.Value is string strValue && strValue.StartsWith("modifier:"))
            {
                var modifierKey = strValue.Replace("modifier:", "");
                formulaParams[param.Key] = context.Modifiers.GetValueOrDefault(modifierKey, 0f);
            }
            else if (param.Value is string strValue2 && strValue2 == "current_damage")
            {
                formulaParams[param.Key] = context.CurrentDamage;
            }
            else
            {
                formulaParams[param.Key] = Convert.ToSingle(param.Value);
            }
        }

        try
        {
            var expression = _mathEngine.BuildFromFormula(formulaName, context.CurrentDamage, formulaParams);
            var result = expression.Build();
            return context.WithDamage(result);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Formula evaluation failed: {ex.Message}");
            return context;
        }
    }

    private DamageContext ExecuteRollCritTier(DamageContext context, BucketOperation op)
    {
        // Sistema de Crítico Multi-Tier usando MathEngine
        // Tier garantido = floor(critChance / 100)
        // Resto = probabilidade de tier extra
        // Fórmula: Dcrit = Dbase × (1 + Tier × (mult - 1))

        var critChance = context.Modifiers.GetValueOrDefault("crit_chance", 0f);
        var critMult = context.Modifiers.GetValueOrDefault("crit_multiplier", 2.0f);

        // Usar MathEngine para calcular tier garantido
        var guaranteedTierExpr = _mathEngine.BuildFromFormula(
            "CRIT_GUARANTEED_TIER",
            0f,
            new Dictionary<string, float> { { "CRIT_CHANCE", critChance } }
        );
        int guaranteedTier = (int)guaranteedTierExpr.Build();

        // Usar MathEngine para calcular chance extra
        var extraChanceExpr = _mathEngine.BuildFromFormula(
            "CRIT_EXTRA_CHANCE",
            0f,
            new Dictionary<string, float> { { "CRIT_CHANCE", critChance } }
        );
        float extraChance = extraChanceExpr.Build();

        // Roll para tier extra
        int finalTier = guaranteedTier;
        if (_randomProvider.NextDouble() * 100 < extraChance)
        {
            finalTier++;
        }

        // Usar MathEngine para calcular multiplicador de dano crítico
        var critMultiplierExpr = _mathEngine.BuildFromFormula(
            "CRIT_DAMAGE_MULTIPLIER",
            0f,
            new Dictionary<string, float> 
            { 
                { "CRIT_TIER", finalTier },
                { "CRIT_MULT", critMult }
            }
        );
        float damageMultiplier = critMultiplierExpr.Build();

        // Aplicar multiplicador ao dano atual
        float critDamage = context.CurrentDamage * damageMultiplier;

        // Adicionar metadata (tier apenas, sem cores - engine é agnóstica)
        var newContext = context.WithDamage(critDamage);
        newContext = newContext.WithMetadata("crit_tier", finalTier);

        _logger.LogDebug($"Critical roll: {critChance:F1}% chance → Tier {finalTier} (guaranteed: {guaranteedTier}, extra chance: {extraChance:F1}%)");

        return newContext;
    }

    private DamageContext ExecuteSetModifier(DamageContext context, BucketOperation op)
    {
        var result = ResolveValueSafe(context, op.Source);
        if (result.IsFailure)
        {
            _logger.LogError($"[{_definition.BucketId}] Failed to resolve value in SET_MODIFIER: {result.Error}");
            return context;
        }
        var key = op.Parameters.TryGetValue("key", out var keyObj) ? keyObj.ToString() : op.Source;
        return context.WithModifier(key!, result.Value);
    }

    private DamageContext ExecuteAddToModifier(DamageContext context, BucketOperation op)
    {
        var result = ResolveValueSafe(context, op.Source);
        if (result.IsFailure)
        {
            _logger.LogError($"[{_definition.BucketId}] Failed to resolve value in ADD_TO_MODIFIER: {result.Error}");
            return context;
        }
        var key = op.Parameters.TryGetValue("key", out var keyObj) ? keyObj.ToString() : op.Source;
        var currentValue = context.Modifiers.GetValueOrDefault(key!, 0f);
        return context.WithModifier(key!, currentValue + result.Value);
    }

    private DamageContext ExecuteAddMoreMultiplier(DamageContext context, BucketOperation op)
    {
        var result = ResolveValueSafe(context, op.Source);
        if (result.IsFailure)
        {
            _logger.LogError($"[{_definition.BucketId}] Failed to resolve multiplier in ADD_MORE_MULTIPLIER: {result.Error}");
            return context;
        }
        
        _logger.LogDebug($"Adding more multiplier: {result.Value:F2}x");
        return context.WithMoreMultiplier(result.Value);
    }

    private DamageContext ExecuteApplyMoreMultipliers(DamageContext context, BucketOperation op)
    {
        if (context.MoreMultipliers.Count == 0)
        {
            _logger.LogDebug("No more multipliers to apply");
            return context;
        }

        var finalDamage = context.CurrentDamage;
        foreach (var multiplier in context.MoreMultipliers)
        {
            finalDamage *= multiplier;
            _logger.LogDebug($"Applied more multiplier {multiplier:F2}x → {finalDamage:F2}");
        }

        _logger.LogDebug($"Applied {context.MoreMultipliers.Count} more multipliers: {context.CurrentDamage:F2} → {finalDamage:F2}");
        return context.WithDamage(finalDamage);
    }

    /// <summary>
    /// Resolves a value from source string with explicit error handling using Result pattern.
    /// </summary>
    private Result<float> ResolveValueSafe(DamageContext context, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Result<float>.Failure("Source cannot be empty");

        // "constant:123.45" - valor literal com prefixo
        if (source.StartsWith("constant:"))
        {
            var valueStr = source.Replace("constant:", "");
            if (float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var constantValue))
                return Result<float>.Success(constantValue);
            return Result<float>.Failure($"Invalid constant format: {source}");
        }

        // "modifier:key" - valor de um modifier
        if (source.StartsWith("modifier:"))
        {
            var key = source.Replace("modifier:", "");
            if (context.Modifiers.TryGetValue(key, out var value))
                return Result<float>.Success(value);
            return Result<float>.Failure($"Modifier '{key}' not found in context");
        }

        // "current_damage" - dano atual
        if (source == "current_damage")
            return Result<float>.Success(context.CurrentDamage);

        // "123.45" - valor literal sem prefixo
        if (float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
            return Result<float>.Success(literal);

        return Result<float>.Failure($"Could not resolve value from source: {source}");
    }

    [Obsolete("Use ResolveValueSafe for better error handling. This method will be removed in v2.0.")]
    private float ResolveValue(DamageContext context, string source)
    {
        var result = ResolveValueSafe(context, source);
        if (result.IsFailure)
        {
            _logger.LogWarning($"[{_definition.BucketId}] {result.Error} (returning 0)");
            return 0f;
        }
        return result.Value;
    }

    private void EmitBucketProcessedEvent(float inputDamage, DamageContext outputContext)
    {
        var evt = new Events.BucketProcessedEvent
        {
            BucketId = _definition.BucketId,
            DamageBefore = inputDamage,
            DamageAfter = outputContext.CurrentDamage,
            Metadata = new Dictionary<string, object>(outputContext.Metadata)
        };
        
        _eventBus.Publish(evt);
        _logger.LogDebug($"[Event] BucketProcessed: {_definition.BucketId} ({inputDamage:F2} → {outputContext.CurrentDamage:F2})");
    }
}
