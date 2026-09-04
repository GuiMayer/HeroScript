using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Calculadora baseada em velocidade: Entidades com maior velocidade agem primeiro
/// Recalcula a cada turno
/// </summary>
public class SpeedBasedTurnOrderCalculator : ITurnOrderCalculator
{
    private readonly ILogger? _logger;
    private readonly string _orderResourceId;
    private readonly float? _missingResourceValue;
    
    public TurnStrategy Strategy => TurnStrategy.SPEED_BASED;
    
    public SpeedBasedTurnOrderCalculator(
        string orderResourceId,
        float? missingResourceValue = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderResourceId);
        if (missingResourceValue.HasValue && !float.IsFinite(missingResourceValue.Value))
            throw new ArgumentOutOfRangeException(nameof(missingResourceValue));
        _orderResourceId = orderResourceId;
        _missingResourceValue = missingResourceValue;
        _logger = logger;
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        var entities = new List<(string EntityId, float Value)>();
        foreach (var entity in state.GetAllEntities().OrderBy(item => item.EntityId, StringComparer.Ordinal))
        {
            var value = GetOrderValue(entity);
            if (value.IsFailure)
                return Result<List<string>>.Failure(value.Error);
            entities.Add((entity.EntityId, value.Value));
        }

        var turnOrder = entities
            .OrderByDescending(e => e.Value)
            .ThenBy(e => e.EntityId, StringComparer.Ordinal)
            .Select(e => e.EntityId)
            .ToList();
        
        _logger?.LogDebug(
            $"Resource-based turn order calculated from {_orderResourceId}: " +
            string.Join(", ", turnOrder.Select(id => $"{id}({entities.First(e => e.EntityId == id).Value})")));
        
        return Result<List<string>>.Success(turnOrder);
    }
    
    public Result Initialize(CombatState state)
    {
        _logger?.LogDebug("Speed-based turn order calculator initialized");
        return Result.Success();
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Velocidade pode mudar, mas recalculamos a cada turno de qualquer forma
        return Result.Success();
    }
    
    private Result<float> GetOrderValue(CombatEntity entity)
    {
        if (entity.ResourceState.Resources.TryGetValue(_orderResourceId, out var resource))
            return Result<float>.Success(resource.Current);
        return _missingResourceValue.HasValue
            ? Result<float>.Success(_missingResourceValue.Value)
            : Result<float>.Failure(
                $"Turn-order resource '{_orderResourceId}' is missing from actor '{entity.EntityId}'");
    }
}
