using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public class FixedTurnOrderCalculatorTests
{
    private readonly ILogger _logger;
    
    public FixedTurnOrderCalculatorTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldPreserveDeclaredActorOrder()
    {
        // Arrange
        var calculator = new FixedTurnOrderCalculator(_logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2", "enemy3" });
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        Assert.Equal("hero1", result.Value[0]);
        Assert.Equal("enemy1", result.Value[1]);
        Assert.Equal("enemy2", result.Value[2]);
        Assert.Equal("enemy3", result.Value[3]);
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldBeConsistent_AcrossMultipleCalls()
    {
        // Arrange
        var calculator = new FixedTurnOrderCalculator(_logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2" });
        
        // Act
        var result1 = calculator.CalculateTurnOrder(state);
        var result2 = calculator.CalculateTurnOrder(state);
        var result3 = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.True(result3.IsSuccess);
        Assert.Equal(result1.Value, result2.Value);
        Assert.Equal(result2.Value, result3.Value);
    }
    
    [Fact]
    public void Initialize_ShouldSucceed()
    {
        // Arrange
        var calculator = new FixedTurnOrderCalculator(_logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1" });
        
        // Act
        var result = calculator.Initialize(state);
        
        // Assert
        Assert.True(result.IsSuccess);
    }
    
    [Fact]
    public void UpdateAfterAction_ShouldSucceed()
    {
        // Arrange
        var calculator = new FixedTurnOrderCalculator(_logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1" });
        
        // Act
        var result = calculator.UpdateAfterAction(state, "hero1");
        
        // Assert
        Assert.True(result.IsSuccess);
    }
    
    [Fact]
    public void Strategy_ShouldReturnFixed()
    {
        // Arrange
        var calculator = new FixedTurnOrderCalculator(_logger);
        
        // Act & Assert
        Assert.Equal(TurnStrategy.FIXED, calculator.Strategy);
    }
    
    private CombatState CreateTestCombatState(string heroId, string[] enemyIds)
    {
        return TurnOrderTestHelper.CreateTestCombatState(heroId, enemyIds);
    }
}
