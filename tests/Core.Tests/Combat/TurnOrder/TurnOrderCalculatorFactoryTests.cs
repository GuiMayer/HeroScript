using Core.Combat.TurnOrder;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public class TurnOrderCalculatorFactoryTests
{
    private readonly ILogger _logger;
    
    public TurnOrderCalculatorFactoryTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
    }
    
    [Fact]
    public void CreateCalculator_WithFixedStrategy_ShouldReturnFixedCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.FIXED));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<FixedTurnOrderCalculator>(result.Value);
        Assert.Equal(TurnStrategy.FIXED, result.Value.Strategy);
    }
    
    [Fact]
    public void CreateCalculator_WithSpeedBasedStrategy_ShouldReturnSpeedBasedCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.SPEED_BASED));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<SpeedBasedTurnOrderCalculator>(result.Value);
        Assert.Equal(TurnStrategy.SPEED_BASED, result.Value.Strategy);
    }
    
    [Fact]
    public void CreateCalculator_WithInitiativeStrategy_ShouldReturnInitiativeCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.INITIATIVE));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<InitiativeTurnOrderCalculator>(result.Value);
        Assert.Equal(TurnStrategy.INITIATIVE, result.Value.Strategy);
    }
    
    [Fact]
    public void CreateCalculator_WithATBStrategy_ShouldReturnATBCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.ATB));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<ATBTurnOrderCalculator>(result.Value);
        Assert.Equal(TurnStrategy.ATB, result.Value.Strategy);
    }
    
    [Fact]
    public void CreateCalculator_WithConditionalStrategy_ShouldReturnConditionalCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.CONDITIONAL));
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<ConditionalTurnOrderCalculator>(result.Value);
        Assert.Equal(TurnStrategy.CONDITIONAL, result.Value.Strategy);
    }
    
    [Fact]
    public void CreateConditionalCalculator_WithCustomFunction_ShouldReturnCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        var customFunction = new Func<Core.Combat.Models.CombatState, List<string>>(
            state => new List<string> { "test" }
        );
        
        // Act
        var result = factory.CreateConditionalCalculator(customFunction);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<ConditionalTurnOrderCalculator>(result.Value);
    }
    
    [Fact]
    public void CreateCalculator_WithCustomATBConfiguration_ShouldReturnCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.ATB) with
        {
            AtbFillRate = 25f
        });
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<ATBTurnOrderCalculator>(result.Value);
    }
    
    [Fact]
    public void CreateCalculator_WithNegativeATBFillRate_ShouldReturnFailure()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.ATB) with
        {
            AtbFillRate = -5f
        });
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Failed to create calculator", result.Error);
    }
    
    [Fact]
    public void CreateCalculator_WithZeroATBFillRate_ShouldReturnFailure()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateCalculator(Configuration(TurnStrategy.ATB) with
        {
            AtbFillRate = 0f
        });
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Failed to create calculator", result.Error);
    }

    private static TurnOrderConfiguration Configuration(TurnStrategy strategy) => new()
    {
        Strategy = strategy,
        OrderResourceId = "speed",
        MissingResourceValue = 0,
        InitiativeDieSides = 20,
        InitiativeResourcePerModifier = 2,
        AtbFillRate = 10,
        AtbReferenceResourceValue = 10,
        AtbReadyThreshold = 100,
        PriorityResourceId = "health",
        PriorityThreshold = 0.3f
    };
}
