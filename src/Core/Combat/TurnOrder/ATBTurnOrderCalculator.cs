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
    private readonly Dictionary<string, float> _atbGauges;
    private readonly float _atbFillRate;
    
    public TurnStrategy Strategy => TurnStrategy.ATB;
    
    /// <summary>
    /// Construtor
    /// </summary>
    /// <param name="atbFillRate">Taxa base de preenchimento da barra ATB (padrão: 10.0)</param>
    /// <param name="logger">Logger opcional</param>
    public ATBTurnOrderCalculator(float atbFillRate = 10f, ILogger? logger = null)
    {
        _logger = logger;
        _atbGauges = new Dictionary<string, float>();
        _atbFillRate = atbFillRate;
    }
    
    public Result<List<string>> CalculateTurnOrder(CombatState state)
    {
        // Preencher as barras ATB de todas as entidades
        FillATBGauges(state);
        
        // Retornar entidades que estão prontas para agir (ATB >= 100)
        var readyEntities = _atbGauges
            .Where(kvp => kvp.Value >= 100f)
            .OrderByDescending(kvp => kvp.Value) // Quem tem mais ATB age primeiro
            .Select(kvp => kvp.Key)
            .ToList();
        
        if (readyEntities.Count == 0)
        {
            // Ninguém está pronto, retornar vazio
            _logger?.LogDebug("No entities ready to act (ATB < 100)");
            return Result<List<string>>.Success(new List<string>());
        }
        
        _logger?.LogDebug($"ATB turn order: {string.Join(", ", readyEntities.Select(id => $"{id}({_atbGauges[id]:F1})"))}");
        
        return Result<List<string>>.Success(readyEntities);
    }
    
    public Result Initialize(CombatState state)
    {
        _atbGauges.Clear();
        
        // Inicializar barras ATB
        _atbGauges[state.Hero.EntityId] = 0f;
        
        foreach (var enemy in state.Enemies)
        {
            _atbGauges[enemy.EntityId] = 0f;
        }
        
        _logger?.LogDebug($"ATB calculator initialized with {_atbGauges.Count} entities");
        
        return Result.Success();
    }
    
    public Result UpdateAfterAction(CombatState state, string actorId)
    {
        // Após agir, resetar a barra ATB da entidade
        if (_atbGauges.ContainsKey(actorId))
        {
            _atbGauges[actorId] = 0f;
            _logger?.LogDebug($"Reset ATB gauge for {actorId}");
        }
        
        return Result.Success();
    }
    
    private void FillATBGauges(CombatState state)
    {
        // Preencher barra do hero
        if (_atbGauges.ContainsKey(state.Hero.EntityId))
        {
            var speed = GetEntitySpeed(state.Hero);
            var fillAmount = _atbFillRate * (speed / 10f); // Normalizado para speed base de 10
            _atbGauges[state.Hero.EntityId] += fillAmount;
        }
        
        // Preencher barras dos inimigos
        foreach (var enemy in state.Enemies)
        {
            if (_atbGauges.ContainsKey(enemy.EntityId))
            {
                var speed = GetEntitySpeed(enemy);
                var fillAmount = _atbFillRate * (speed / 10f);
                _atbGauges[enemy.EntityId] += fillAmount;
            }
        }
    }
    
    private float GetEntitySpeed(CombatEntity entity)
    {
        // Tentar obter velocidade do recurso "speed"
        if (entity.ResourceState.Resources.TryGetValue("speed", out var speedPool))
        {
            return speedPool.Current;
        }
        
        // Fallback: usar um valor padrão
        return entity.EntityId.StartsWith("hero") ? 10f : 5f;
    }
}
