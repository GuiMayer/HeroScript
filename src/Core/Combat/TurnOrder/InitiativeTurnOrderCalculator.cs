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
    private readonly Random _random;
    private List<string>? _fixedTurnOrder;
    
    public TurnStrategy Strategy => TurnStrategy.INITIATIVE;
    
    public InitiativeTurnOrderCalculator(ILogger? logger = null, Random? random = null)
    {
        _logger = logger;
        _random = random ?? new Random();
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        // Se já temos ordem fixa, retornar ela
        if (_fixedTurnOrder != null)
        {
            return Result<List<string>>.Success(_fixedTurnOrder);
        }
        
        // Caso contrário, calcular nova ordem (não deveria acontecer após Initialize)
        _logger?.LogWarning("CalculateTurnOrder called before Initialize - calculating new order");
        var initResult = Initialize(state);
        if (initResult.IsFailure)
        {
            return Result<List<string>>.Failure(initResult.Error);
        }
        
        return Result<List<string>>.Success(_fixedTurnOrder!);
    }
    
    public Result Initialize(CombatState state)
    {
        var entities = new List<(string EntityId, int Initiative)>();
        
        // Rolar iniciativa para o hero
        var heroInitiative = RollInitiative(state.Hero);
        entities.Add((state.Hero.EntityId, heroInitiative));
        
        // Rolar iniciativa para os inimigos
        foreach (var enemy in state.Enemies)
        {
            var enemyInitiative = RollInitiative(enemy);
            entities.Add((enemy.EntityId, enemyInitiative));
        }
        
        // Ordenar por iniciativa (maior primeiro)
        _fixedTurnOrder = entities
            .OrderByDescending(e => e.Initiative)
            .ThenBy(e => e.EntityId) // Desempate por ID
            .Select(e => e.EntityId)
            .ToList();
        
        _logger?.LogDebug($"Initiative turn order calculated: {string.Join(", ", _fixedTurnOrder.Select(id => $"{id}({entities.First(e => e.EntityId == id).Initiative})"))}");
        
        return Result.Success();
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Ordem de iniciativa não muda após ações
        return Result.Success();
    }
    
    private int RollInitiative(CombatEntity entity)
    {
        // Rolar 1d20 + modificador de velocidade
        var roll = _random.Next(1, 21); // 1d20
        
        // Tentar obter modificador de velocidade
        var speedModifier = 0;
        if (entity.ResourceState.Resources.TryGetValue("speed", out var speedPool))
        {
            // Cada 2 pontos de velocidade dá +1 de modificador
            speedModifier = (int)(speedPool.Current / 2);
        }
        
        return roll + speedModifier;
    }
}
