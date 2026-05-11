using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Calculadora de ordem fixa: Hero sempre age primeiro, depois inimigos na ordem de criação
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
        var turnOrder = new List<string>();
        
        // Hero sempre age primeiro
        turnOrder.Add(state.Hero.EntityId);
        
        // Inimigos agem na ordem de criação
        foreach (var enemy in state.Enemies)
        {
            turnOrder.Add(enemy.EntityId);
        }
        
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
