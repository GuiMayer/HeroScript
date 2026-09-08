using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Deterministic fixed order based on canonical actor instance IDs.
/// </summary>
public class FixedTurnOrderCalculator : ITurnOrderCalculator
{
    private readonly ILogger? _logger;
    
    public TurnStrategy Strategy => TurnStrategy.FIXED;
    
    public FixedTurnOrderCalculator(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        var turnOrder = state.GetAllActors()
            .Select(actor => actor.InstanceId)
            .ToList();
        
        _logger?.LogDebug($"Fixed turn order calculated: {string.Join(", ", turnOrder)}");
        
        return Result<List<string>>.Success(turnOrder);
    }
    
    public Result Initialize(CombatState state)
    {
        _logger?.LogDebug("Fixed turn order calculator initialized");
        return Result.Success();
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Ordem fixa não precisa de atualização após ações
        return Result.Success();
    }
}
