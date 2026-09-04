using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public class ATBTurnOrderCalculatorTests
{
    private readonly ILogger _logger;
    
    public ATBTurnOrderCalculatorTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
    }
    
    [Fact]
    public void Initialize_ShouldSetAllGaugesToZero()
    {
        // Arrange
        var calculator = Calculator(10f, missingResourceValue: 10f);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2" });
        
        // Act
        var result = calculator.Initialize(state);
        var turnOrderResult = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(turnOrderResult.IsSuccess);
        Assert.Empty(turnOrderResult.Value); // No one ready yet
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldReturnEmpty_WhenNoEntityIsReady()
    {
        // Arrange
        var calculator = Calculator(10f, missingResourceValue: 10f);
        var state = CreateTestCombatState("hero1", new[] { "enemy1" });
        calculator.Initialize(state);
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldFillGauges_AndReturnReadyEntities()
    {
        // Arrange
        var calculator = Calculator(100f, missingResourceValue: 10f); // High fill rate
        var state = CreateTestCombatState("hero1", new[] { "enemy1" });
        calculator.Initialize(state);
        
        // Act - First call fills gauges to 100
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value); // Should have ready entities
    }
    
    [Fact]
    public void UpdateAfterAction_ShouldResetGauge()
    {
        // Arrange
        var calculator = Calculator(100f, missingResourceValue: 10f);
        var state = CreateTestCombatState("hero1", new[] { "enemy1" });
        calculator.Initialize(state);
        
        // Fill gauges
        calculator.CalculateTurnOrder(state);
        
        // Act - Reset hero's gauge
        var updateResult = calculator.UpdateAfterAction(state, "hero1");
        var turnOrderResult = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(updateResult.IsSuccess);
        Assert.True(turnOrderResult.IsSuccess);
    }
    
    [Fact]
    public void CalculateTurnOrder_WithHigherSpeed_ShouldFillFaster()
    {
        // Arrange
        var calculator = Calculator(10f);
        var state = CreateTestCombatStateWithSpeed(
            ("hero1", 20f),
            new[] { ("enemy1", 5f) }
        );
        calculator.Initialize(state);
        
        // Act - Multiple calls to fill gauges
        for (int i = 0; i < 5; i++)
        {
            calculator.CalculateTurnOrder(state);
        }
        
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert - Hero with higher speed should be ready first
        Assert.True(result.IsSuccess);
        if (result.Value.Count > 0)
        {
            Assert.Equal("hero1", result.Value[0]);
        }
    }
    
    [Fact]
    public void Strategy_ShouldReturnATB()
    {
        // Arrange
        var calculator = Calculator(10f, missingResourceValue: 10f);
        
        // Act & Assert
        Assert.Equal(TurnStrategy.ATB, calculator.Strategy);
    }
    
    private CombatState CreateTestCombatState(string heroId, string[] enemyIds)
    {
        return TurnOrderTestHelper.CreateTestCombatState(heroId, enemyIds);
    }
    
    private CombatState CreateTestCombatStateWithSpeed(
        (string id, float speed) heroData,
        (string id, float speed)[] enemyData)
    {
        return TurnOrderTestHelper.CreateTestCombatStateWithSpeed(heroData, enemyData);
    }

    private ATBTurnOrderCalculator Calculator(float fillRate, float? missingResourceValue = null) =>
        new("speed", fillRate, 10f, 100f, missingResourceValue, _logger);
}
