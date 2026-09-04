using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Calculadora Active Time Battle: Cada entidade tem uma barra de tempo que preenche
/// Quando cheia, a entidade pode agir
/// </summary>
public class ATBTurnOrderCalculator : ITurnOrderCalculator
{
    private readonly ILogger? _logger;
    private readonly string _rateResourceId;
    private readonly float _atbFillRate;
    private readonly float _referenceResourceValue;
    private readonly float _readyThreshold;
    private readonly float? _missingResourceValue;
    
    public TurnStrategy Strategy => TurnStrategy.ATB;
    
    /// <summary>
    /// Construtor
    /// </summary>
    /// <param name="atbFillRate">Taxa base de preenchimento da barra ATB (padrão: 10.0)</param>
    /// <param name="logger">Logger opcional</param>
    public ATBTurnOrderCalculator(
        string rateResourceId,
        float atbFillRate,
        float referenceResourceValue,
        float readyThreshold,
        float? missingResourceValue = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rateResourceId);
        if (!float.IsFinite(atbFillRate) || atbFillRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(atbFillRate));
        if (!float.IsFinite(referenceResourceValue) || referenceResourceValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceResourceValue));
        if (!float.IsFinite(readyThreshold) || readyThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(readyThreshold));
        if (missingResourceValue.HasValue && !float.IsFinite(missingResourceValue.Value))
            throw new ArgumentOutOfRangeException(nameof(missingResourceValue));
        _rateResourceId = rateResourceId;
        _logger = logger;
        _atbFillRate = atbFillRate;
        _referenceResourceValue = referenceResourceValue;
        _readyThreshold = readyThreshold;
        _missingResourceValue = missingResourceValue;
    }

    public Result<CombatState> InitializeState(CombatState state)
    {
        var gauges = state.GetAllEntities()
            .ToImmutableDictionary(entity => entity.EntityId, _ => 0f, StringComparer.Ordinal);
        _logger?.LogDebug($"ATB calculator initialized with {gauges.Count} entities");
        return Result<CombatState>.Success(state with { TurnOrderValues = gauges });
    }

    public Result<TurnOrderTransition> Calculate(CombatState state)
    {
        var gauges = state.TurnOrderValues;
        if (gauges.Count == 0)
        {
            var initialized = InitializeState(state);
            if (initialized.IsFailure)
                return Result<TurnOrderTransition>.Failure(initialized.Error);
            state = initialized.Value;
            gauges = state.TurnOrderValues;
        }

        foreach (var entity in state.GetAllEntities())
        {
            var rate = GetRateValue(entity);
            if (rate.IsFailure)
                return Result<TurnOrderTransition>.Failure(rate.Error);
            var current = gauges.GetValueOrDefault(entity.EntityId);
            gauges = gauges.SetItem(
                entity.EntityId,
                current + _atbFillRate * (rate.Value / _referenceResourceValue));
        }

        var order = gauges
            .Where(pair => pair.Value >= _readyThreshold)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .ToList();
        var next = state with { TurnOrderValues = gauges, TurnOrder = order };
        return Result<TurnOrderTransition>.Success(new TurnOrderTransition(next, order));
    }

    public Result<CombatState> UpdateStateAfterAction(CombatState state, string actorId)
    {
        var gauges = state.TurnOrderValues.ContainsKey(actorId)
            ? state.TurnOrderValues.SetItem(actorId, 0f)
            : state.TurnOrderValues;
        return Result<CombatState>.Success(state with { TurnOrderValues = gauges });
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        var result = Calculate(state);
        return result.IsSuccess
            ? Result<List<string>>.Success(result.Value.Order.ToList())
            : Result<List<string>>.Failure(result.Error);
    }
    
    public Result Initialize(CombatState state)
    {
        return InitializeState(state).IsSuccess ? Result.Success() : Result.Failure("Failed to initialize ATB");
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        return UpdateStateAfterAction(state, actorId).IsSuccess
            ? Result.Success()
            : Result.Failure("Failed to update ATB");
    }
    
    private Result<float> GetRateValue(CombatEntity entity)
    {
        if (entity.ResourceState.Resources.TryGetValue(_rateResourceId, out var resource))
            return Result<float>.Success(resource.Current);
        return _missingResourceValue.HasValue
            ? Result<float>.Success(_missingResourceValue.Value)
            : Result<float>.Failure(
                $"ATB rate resource '{_rateResourceId}' is missing from actor '{entity.EntityId}'");
    }
}
