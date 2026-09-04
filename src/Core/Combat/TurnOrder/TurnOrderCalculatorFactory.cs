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
    public Result<ITurnOrderCalculator> CreateCalculator(TurnOrderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            ITurnOrderCalculator calculator = configuration.Strategy switch
            {
                TurnStrategy.FIXED => new FixedTurnOrderCalculator(_logger),
                TurnStrategy.SPEED_BASED => new SpeedBasedTurnOrderCalculator(
                    configuration.OrderResourceId!,
                    configuration.MissingResourceValue,
                    _logger),
                TurnStrategy.INITIATIVE => new InitiativeTurnOrderCalculator(
                    configuration.OrderResourceId!,
                    configuration.InitiativeResourcePerModifier,
                    configuration.InitiativeDieSides,
                    configuration.MissingResourceValue,
                    _logger),
                TurnStrategy.ATB => new ATBTurnOrderCalculator(
                    configuration.OrderResourceId!,
                    configuration.AtbFillRate,
                    configuration.AtbReferenceResourceValue,
                    configuration.AtbReadyThreshold,
                    configuration.MissingResourceValue,
                    _logger),
                TurnStrategy.CONDITIONAL => ConditionalTurnOrderCalculator.CreateHybridCalculator(
                    configuration.OrderResourceId!,
                    configuration.PriorityResourceId!,
                    configuration.PriorityThreshold,
                    configuration.MissingResourceValue,
                    _logger),
                _ => throw new ArgumentException(
                    $"Unknown or unspecified turn strategy: {configuration.Strategy}")
            };
            
            _logger?.LogDebug($"Created turn order calculator for strategy: {configuration.Strategy}");
            
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
    
}

/// <summary>
/// Explicit configuration for the host-level combat adapter. Canonical runs
/// capture their combat-flow policies separately; direct combats still need a
/// complete deterministic turn-order definition.
/// </summary>
public sealed record TurnOrderConfiguration
{
    public TurnStrategy Strategy { get; set; }
    public string? OrderResourceId { get; set; }
    public float? MissingResourceValue { get; set; }
    public int InitiativeDieSides { get; set; }
    public float InitiativeResourcePerModifier { get; set; }
    public float AtbFillRate { get; set; }
    public float AtbReferenceResourceValue { get; set; }
    public float AtbReadyThreshold { get; set; }
    public string? PriorityResourceId { get; set; }
    public float PriorityThreshold { get; set; }
}
