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
        var result = factory.CreateCalculator(TurnStrategy.FIXED);
        
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
        var result = factory.CreateCalculator(TurnStrategy.SPEED_BASED);
        
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
        var result = factory.CreateCalculator(TurnStrategy.INITIATIVE);
        
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
        var result = factory.CreateCalculator(TurnStrategy.ATB);
        
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
        var result = factory.CreateCalculator(TurnStrategy.CONDITIONAL);
        
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
    public void CreateATBCalculator_WithCustomFillRate_ShouldReturnCalculator()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateATBCalculator(25f);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.IsType<ATBTurnOrderCalculator>(result.Value);
    }
    
    [Fact]
    public void CreateATBCalculator_WithNegativeFillRate_ShouldReturnFailure()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateATBCalculator(-5f);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("must be positive", result.Error);
    }
    
    [Fact]
    public void CreateATBCalculator_WithZeroFillRate_ShouldReturnFailure()
    {
        // Arrange
        var factory = new TurnOrderCalculatorFactory(_logger);
        
        // Act
        var result = factory.CreateATBCalculator(0f);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("must be positive", result.Error);
    }
}
