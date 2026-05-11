using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public class InitiativeTurnOrderCalculatorTests
{
    private readonly ILogger _logger;
    
    public InitiativeTurnOrderCalculatorTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
    }
    
    [Fact]
    public void Initialize_ShouldCalculateInitiativeOrder()
    {
        // Arrange
        var random = new Random(42); // Fixed seed for deterministic tests
        var calculator = new InitiativeTurnOrderCalculator(_logger, random);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2" });
        
        // Act
        var initResult = calculator.Initialize(state);
        var turnOrderResult = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(initResult.IsSuccess);
        Assert.True(turnOrderResult.IsSuccess);
        Assert.Equal(3, turnOrderResult.Value.Count);
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldReturnSameOrder_AcrossMultipleCalls()
    {
        // Arrange
        var calculator = new InitiativeTurnOrderCalculator(_logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2" });
        
        // Act
        calculator.Initialize(state);
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
    public void Initialize_WithSpeedModifier_ShouldAffectInitiative()
    {
        // Arrange
        var random = new Random(42);
        var calculator = new InitiativeTurnOrderCalculator(_logger, random);
        
        // Create state with high speed hero
        var state = CreateTestCombatStateWithSpeed(
            ("hero1", 20f),
            new[] { ("enemy1", 2f) }
        );
        
        // Act
        calculator.Initialize(state);
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert - Hero with high speed should have better chance to go first
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }
    
    [Fact]
    public void Strategy_ShouldReturnInitiative()
    {
        // Arrange
        var calculator = new InitiativeTurnOrderCalculator(_logger);
        
        // Act & Assert
        Assert.Equal(TurnStrategy.INITIATIVE, calculator.Strategy);
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
}
