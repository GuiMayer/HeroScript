using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Calculadora baseada em iniciativa: Rola dados no início do combate
/// Ordem permanece fixa durante todo o combate
/// </summary>
public class InitiativeTurnOrderCalculator : ITurnOrderCalculator
{
    private readonly ILogger? _logger;
    private readonly string _modifierResourceId;
    private readonly float _resourcePerModifier;
    private readonly int _dieSides;
    private readonly float? _missingResourceValue;

    public TurnStrategy Strategy => TurnStrategy.INITIATIVE;
    
    public InitiativeTurnOrderCalculator(
        string modifierResourceId,
        float resourcePerModifier,
        int dieSides,
        float? missingResourceValue = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modifierResourceId);
        if (!float.IsFinite(resourcePerModifier) || resourcePerModifier <= 0)
            throw new ArgumentOutOfRangeException(nameof(resourcePerModifier));
        if (dieSides <= 0)
            throw new ArgumentOutOfRangeException(nameof(dieSides));
        if (missingResourceValue.HasValue && !float.IsFinite(missingResourceValue.Value))
            throw new ArgumentOutOfRangeException(nameof(missingResourceValue));
        _modifierResourceId = modifierResourceId;
        _resourcePerModifier = resourcePerModifier;
        _dieSides = dieSides;
        _missingResourceValue = missingResourceValue;
        _logger = logger;
    }

    public Result<CombatState> InitializeState(CombatState state)
    {
        var context = state.Determinism;
        var entities = new List<(string InstanceId, int Initiative)>();

        var actors = state.GetAllActors()
            .OrderBy(entity => entity.InstanceId, StringComparer.Ordinal)
            .ToArray();
        var modifiers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entity in actors)
        {
            var modifier = GetResourceModifier(entity);
            if (modifier.IsFailure)
                return Result<CombatState>.Failure(modifier.Error);
            modifiers[entity.InstanceId] = modifier.Value;
        }

        foreach (var entity in actors)
        {
            var roll = context.DrawInt32(_dieSides);
            context = roll.Context;
            entities.Add((entity.InstanceId, roll.Value + 1 + modifiers[entity.InstanceId]));
        }

        var order = entities
            .OrderByDescending(entity => entity.Initiative)
            .ThenBy(entity => entity.InstanceId, StringComparer.Ordinal)
            .Select(entity => entity.InstanceId)
            .ToList();

        _logger?.LogDebug($"Initiative turn order calculated: {string.Join(", ", order)}");
        return Result<CombatState>.Success(state with
        {
            Determinism = context,
            TurnOrder = order
        });
    }

    public Result<TurnOrderTransition> Calculate(CombatState state)
    {
        if (state.TurnOrder is { Count: > 0 })
            return Result<TurnOrderTransition>.Success(new TurnOrderTransition(state, state.TurnOrder));

        var initialized = InitializeState(state);
        return initialized.IsSuccess
            ? Result<TurnOrderTransition>.Success(
                new TurnOrderTransition(initialized.Value, initialized.Value.TurnOrder ?? []))
            : Result<TurnOrderTransition>.Failure(initialized.Error);
    }

    public Result<CombatState> UpdateStateAfterAction(CombatState state, string actorId) =>
        Result<CombatState>.Success(state);

    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        var result = Calculate(state);
        return result.IsSuccess
            ? Result<List<string>>.Success(result.Value.Order.ToList())
            : Result<List<string>>.Failure(result.Error);
    }
    
    public Result Initialize(CombatState state)
    {
        return InitializeState(state).IsSuccess ? Result.Success() : Result.Failure("Failed to initialize initiative");
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Ordem de iniciativa não muda após ações
        return Result.Success();
    }
    
    private Result<int> GetResourceModifier(CombatActorState entity)
    {
        if (entity.ResourceState.Resources.TryGetValue(_modifierResourceId, out var resource))
            return Result<int>.Success((int)(resource.Current / _resourcePerModifier));
        return _missingResourceValue.HasValue
            ? Result<int>.Success((int)(_missingResourceValue.Value / _resourcePerModifier))
            : Result<int>.Failure(
                $"Initiative resource '{_modifierResourceId}' is missing from actor '{entity.InstanceId}'");
    }
}
