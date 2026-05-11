using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Calculadora condicional: Usa regras customizadas baseadas em condições
/// Permite lógica complexa de ordenação
/// </summary>
public class ConditionalTurnOrderCalculator : ITurnOrderCalculator
{
    private readonly ILogger? _logger;
    private readonly Func<CombatState, List<string>> _customOrderFunction;
    
    public TurnStrategy Strategy => TurnStrategy.CONDITIONAL;
    
    /// <summary>
    /// Construtor
    /// </summary>
    /// <param name="customOrderFunction">Função customizada que calcula a ordem de turnos</param>
    /// <param name="logger">Logger opcional</param>
    public ConditionalTurnOrderCalculator(
        Func<CombatState, List<string>> customOrderFunction,
        ILogger? logger = null)
    {
        _customOrderFunction = customOrderFunction ?? throw new ArgumentNullException(nameof(customOrderFunction));
        _logger = logger;
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        try
        {
            var turnOrder = _customOrderFunction(state);
            
            _logger?.LogDebug($"Conditional turn order calculated: {string.Join(", ", turnOrder)}");
            
            return Result<List<string>>.Success(turnOrder);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error calculating conditional turn order: {ex.Message}");
            return Result<List<string>>.Failure($"Failed to calculate conditional turn order: {ex.Message}");
        }
    }
    
    public Result Initialize(CombatState state)
    {
        _logger?.LogDebug("Conditional turn order calculator initialized");
        return Result.Success();
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Ordem condicional pode mudar a cada turno, mas não precisa de atualização específica
        return Result.Success();
    }
    
    /// <summary>
    /// Cria uma calculadora condicional com regra baseada em saúde
    /// Entidades com menos saúde agem primeiro
    /// </summary>
    public static ConditionalTurnOrderCalculator CreateHealthBasedCalculator(ILogger? logger = null)
    {
        return new ConditionalTurnOrderCalculator(state =>
        {
            var entities = new List<(string EntityId, float HealthPercent)>();
            
            // Adicionar hero
            var heroHealth = GetHealthPercent(state.Hero);
            entities.Add((state.Hero.EntityId, heroHealth));
            
            // Adicionar inimigos
            foreach (var enemy in state.Enemies)
            {
                var enemyHealth = GetHealthPercent(enemy);
                entities.Add((enemy.EntityId, enemyHealth));
            }
            
            // Ordenar por saúde (menor primeiro)
            return entities
                .OrderBy(e => e.HealthPercent)
                .ThenBy(e => e.EntityId)
                .Select(e => e.EntityId)
                .ToList();
        }, logger);
    }
    
    /// <summary>
    /// Cria uma calculadora condicional com regra baseada em tipo de entidade
    /// Inimigos agem primeiro, depois o hero
    /// </summary>
    public static ConditionalTurnOrderCalculator CreateEnemiesFirstCalculator(ILogger? logger = null)
    {
        return new ConditionalTurnOrderCalculator(state =>
        {
            var turnOrder = new List<string>();
            
            // Inimigos agem primeiro
            foreach (var enemy in state.Enemies)
            {
                turnOrder.Add(enemy.EntityId);
            }
            
            // Hero age por último
            turnOrder.Add(state.Hero.EntityId);
            
            return turnOrder;
        }, logger);
    }
    
    /// <summary>
    /// Cria uma calculadora condicional com regra híbrida
    /// Usa velocidade, mas dá prioridade a entidades com baixa saúde
    /// </summary>
    public static ConditionalTurnOrderCalculator CreateHybridCalculator(ILogger? logger = null)
    {
        return new ConditionalTurnOrderCalculator(state =>
        {
            var entities = new List<(string EntityId, float Speed, float HealthPercent, bool LowHealth)>();
            
            // Adicionar hero
            var heroSpeed = GetEntitySpeed(state.Hero);
            var heroHealth = GetHealthPercent(state.Hero);
            entities.Add((state.Hero.EntityId, heroSpeed, heroHealth, heroHealth < 0.3f));
            
            // Adicionar inimigos
            foreach (var enemy in state.Enemies)
            {
                var enemySpeed = GetEntitySpeed(enemy);
                var enemyHealth = GetHealthPercent(enemy);
                entities.Add((enemy.EntityId, enemySpeed, enemyHealth, enemyHealth < 0.3f));
            }
            
            // Ordenar: baixa saúde primeiro, depois por velocidade
            return entities
                .OrderByDescending(e => e.LowHealth)
                .ThenByDescending(e => e.Speed)
                .ThenBy(e => e.EntityId)
                .Select(e => e.EntityId)
                .ToList();
        }, logger);
    }
    
    private static float GetHealthPercent(CombatEntity entity)
    {
        if (entity.ResourceState.Resources.TryGetValue("health", out var healthPool))
        {
            if (healthPool.Maximum > 0)
            {
                return healthPool.Current / healthPool.Maximum;
            }
        }
        return 1f;
    }
    
    private static float GetEntitySpeed(CombatEntity entity)
    {
        if (entity.ResourceState.Resources.TryGetValue("speed", out var speedPool))
        {
            return speedPool.Current;
        }
        return entity.EntityId.StartsWith("hero") ? 10f : 5f;
    }
}
