using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Random _random;

    public GenericBucketProcessor(
        BucketDefinition definition,
        IMathEngine mathEngine,
        IEventBus eventBus,
        ILogger logger)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _mathEngine = mathEngine ?? throw new ArgumentNullException(nameof(mathEngine));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = Random.Shared; // Thread-safe random (.NET 6+)
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
                OperationType.SET_TAG => context.WithTag(op.Source),
                OperationType.REMOVE_TAG => context.RemoveTag(op.Source),
                OperationType.SET_MODIFIER => ExecuteSetModifier(context, op),
                OperationType.ADD_TO_MODIFIER => ExecuteAddToModifier(context, op),
                _ => throw new InvalidOperationException($"Unknown operation type: {op.Type}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error executing operation {op.Type} in bucket {_definition.BucketId}: {ex.Message}");
            return context; // Retorna contexto inalterado em caso de erro
        }
    }

    private DamageContext ExecuteAddFlat(DamageContext context, BucketOperation op)
    {
        var value = ResolveValue(context, op.Source);
        return context.WithDamage(context.CurrentDamage + value);
    }

    private DamageContext ExecuteMultiply(DamageContext context, BucketOperation op)
    {
        var multiplier = ResolveValue(context, op.Source);
        return context.WithDamage(context.CurrentDamage * multiplier);
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

        var result = _mathEngine.EvaluateFormula(formulaName, formulaParams);
        if (!result.IsSuccess)
        {
            _logger.LogError($"Formula evaluation failed: {result.Error}");
            return context;
        }

        return context.WithDamage(result.Value);
    }

    private DamageContext ExecuteRollCritTier(DamageContext context, BucketOperation op)
    {
        // Sistema de Crítico Multi-Tier
        // Tier garantido = floor(critChance / 100)
        // Resto = probabilidade de tier extra
        // Fórmula: Dcrit = Dbase × (1 + Tier × (mult - 1))

        var critChance = context.Modifiers.GetValueOrDefault("crit_chance", 0f);
        var critMult = context.Modifiers.GetValueOrDefault("crit_multiplier", 2.0f);

        int guaranteedTier = (int)Math.Floor(critChance / 100f);
        float extraChance = critChance % 100f;

        // Roll para tier extra
        int finalTier = guaranteedTier;
        if (_random.NextDouble() * 100 < extraChance)
        {
            finalTier++;
        }

        // Aplicar fórmula de crítico
        float critDamage = context.CurrentDamage * (1 + finalTier * (critMult - 1));

        // Adicionar metadata (tier apenas, sem cores - engine é agnóstica)
        var newContext = context.WithDamage(critDamage);
        newContext = newContext.WithMetadata("crit_tier", finalTier);

        _logger.LogDebug($"Critical roll: {critChance:F1}% chance → Tier {finalTier} (guaranteed: {guaranteedTier}, extra chance: {extraChance:F1}%)");

        return newContext;
    }

    private DamageContext ExecuteSetModifier(DamageContext context, BucketOperation op)
    {
        var value = ResolveValue(context, op.Source);
        var key = op.Parameters.TryGetValue("key", out var keyObj) ? keyObj.ToString() : op.Source;
        return context.WithModifier(key!, value);
    }

    private DamageContext ExecuteAddToModifier(DamageContext context, BucketOperation op)
    {
        var value = ResolveValue(context, op.Source);
        var key = op.Parameters.TryGetValue("key", out var keyObj) ? keyObj.ToString() : op.Source;
        var currentValue = context.Modifiers.GetValueOrDefault(key!, 0f);
        return context.WithModifier(key!, currentValue + value);
    }

    private float ResolveValue(DamageContext context, string source)
    {
        // Source pode ser:
        // - "modifier:key" - valor de um modifier
        // - "123.45" - valor literal
        // - "current_damage" - dano atual

        if (source.StartsWith("modifier:"))
        {
            var key = source.Replace("modifier:", "");
            return context.Modifiers.GetValueOrDefault(key, 0f);
        }

        if (source == "current_damage")
        {
            return context.CurrentDamage;
        }

        if (float.TryParse(source, out var literal))
        {
            return literal;
        }

        _logger.LogWarning($"Could not resolve value from source: {source}");
        return 0f;
    }

    private void EmitBucketProcessedEvent(float inputDamage, DamageContext outputContext)
    {
        // Evento será criado na FASE 5
        // Por enquanto, apenas log
        _logger.LogDebug($"[Event] BucketProcessed: {_definition.BucketId}");
    }
}
