using Core.Combat.Models;
using Core.Common;
using Core.Logging;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Factory para criar calculadoras de ordem de turnos
/// </summary>
public class TurnOrderCalculatorFactory
{
    private readonly ILogger? _logger;
    
    public TurnOrderCalculatorFactory(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Cria uma calculadora baseada na estratégia especificada
    /// </summary>
    public Result<ITurnOrderCalculator> CreateCalculator(TurnStrategy strategy)
    {
        try
        {
            ITurnOrderCalculator calculator = strategy switch
            {
                TurnStrategy.FIXED => new FixedTurnOrderCalculator(_logger),
                TurnStrategy.SPEED_BASED => new SpeedBasedTurnOrderCalculator(_logger),
                TurnStrategy.INITIATIVE => new InitiativeTurnOrderCalculator(_logger),
                TurnStrategy.ATB => new ATBTurnOrderCalculator(atbFillRate: 10f, _logger),
                TurnStrategy.CONDITIONAL => ConditionalTurnOrderCalculator.CreateHybridCalculator(_logger),
                _ => throw new ArgumentException($"Unknown turn strategy: {strategy}")
            };
            
            _logger?.LogDebug($"Created turn order calculator for strategy: {strategy}");
            
            return Result<ITurnOrderCalculator>.Success(calculator);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to create turn order calculator: {ex.Message}");
            return Result<ITurnOrderCalculator>.Failure($"Failed to create calculator: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Cria uma calculadora condicional customizada
    /// </summary>
    public Result<ITurnOrderCalculator> CreateConditionalCalculator(
        Func<CombatState, List<string>> customOrderFunction)
    {
        try
        {
            var calculator = new ConditionalTurnOrderCalculator(customOrderFunction, _logger);
            
            _logger?.LogDebug("Created custom conditional turn order calculator");
            
            return Result<ITurnOrderCalculator>.Success(calculator);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to create conditional calculator: {ex.Message}");
            return Result<ITurnOrderCalculator>.Failure($"Failed to create conditional calculator: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Cria uma calculadora ATB com taxa de preenchimento customizada
    /// </summary>
    public Result<ITurnOrderCalculator> CreateATBCalculator(float atbFillRate)
    {
        try
        {
            if (atbFillRate <= 0)
            {
                return Result<ITurnOrderCalculator>.Failure("ATB fill rate must be positive");
            }
            
            var calculator = new ATBTurnOrderCalculator(atbFillRate, _logger);
            
            _logger?.LogDebug($"Created ATB calculator with fill rate: {atbFillRate}");
            
            return Result<ITurnOrderCalculator>.Success(calculator);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to create ATB calculator: {ex.Message}");
            return Result<ITurnOrderCalculator>.Failure($"Failed to create ATB calculator: {ex.Message}");
        }
    }
}
