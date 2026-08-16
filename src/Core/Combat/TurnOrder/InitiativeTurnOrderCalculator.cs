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

    public TurnStrategy Strategy => TurnStrategy.INITIATIVE;
    
    public InitiativeTurnOrderCalculator(ILogger? logger = null, Random? random = null)
    {
        _logger = logger;
    }

    public Result<CombatState> InitializeState(CombatState state)
    {
        var context = state.Determinism;
        var entities = new List<(string EntityId, int Initiative)>();

        foreach (var entity in state.GetAllEntities())
        {
            var roll = context.DrawInt32(20);
            context = roll.Context;
            entities.Add((entity.EntityId, roll.Value + 1 + GetSpeedModifier(entity)));
        }

        var order = entities
            .OrderByDescending(entity => entity.Initiative)
            .ThenBy(entity => entity.EntityId, StringComparer.Ordinal)
            .Select(entity => entity.EntityId)
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
    
    private static int GetSpeedModifier(CombatEntity entity)
    {
        var speedModifier = 0;
        if (entity.ResourceState.Resources.TryGetValue("speed", out var speedPool))
        {
            // Cada 2 pontos de velocidade dá +1 de modificador
            speedModifier = (int)(speedPool.Current / 2);
        }
        
        return speedModifier;
    }
}
