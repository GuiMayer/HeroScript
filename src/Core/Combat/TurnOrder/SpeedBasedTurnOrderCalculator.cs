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
    
    public TurnStrategy Strategy => TurnStrategy.SPEED_BASED;
    
    public SpeedBasedTurnOrderCalculator(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        var entities = new List<(string EntityId, float Speed)>();
        
        // Adicionar hero
        var heroSpeed = GetEntitySpeed(state.Hero);
        entities.Add((state.Hero.EntityId, heroSpeed));
        
        // Adicionar inimigos
        foreach (var enemy in state.Enemies)
        {
            var enemySpeed = GetEntitySpeed(enemy);
            entities.Add((enemy.EntityId, enemySpeed));
        }
        
        // Ordenar por velocidade (maior primeiro)
        var turnOrder = entities
            .OrderByDescending(e => e.Speed)
            .ThenBy(e => e.EntityId) // Desempate por ID
            .Select(e => e.EntityId)
            .ToList();
        
        _logger?.LogDebug($"Speed-based turn order calculated: {string.Join(", ", turnOrder.Select(id => $"{id}({entities.First(e => e.EntityId == id).Speed})"))}");
        
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
    
    private float GetEntitySpeed(CombatEntity entity)
    {
        // Tentar obter velocidade do recurso "speed"
        if (entity.ResourceState.Resources.TryGetValue("speed", out var speedPool))
        {
            return speedPool.Current;
        }
        
        // Fallback: usar um valor padrão baseado no tipo de entidade
        // Hero tem velocidade base de 10, inimigos têm 5
        return entity.EntityId.StartsWith("hero") ? 10f : 5f;
    }
}
