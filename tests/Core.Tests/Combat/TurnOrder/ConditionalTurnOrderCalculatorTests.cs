using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public class ConditionalTurnOrderCalculatorTests
{
    private readonly ILogger _logger;
    
    public ConditionalTurnOrderCalculatorTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
    }
    
    [Fact]
    public void CalculateTurnOrder_WithCustomFunction_ShouldUseProvidedLogic()
    {
        // Arrange
        var customOrder = new List<string> { "enemy1", "hero1", "enemy2" };
        var calculator = new ConditionalTurnOrderCalculator(
            state => customOrder,
            _logger
        );
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2" });
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(customOrder, result.Value);
    }
    
    [Fact]
    public void CreateResourceBasedCalculator_ShouldOrderByLowestResourceFirst()
    {
        // Arrange
        var calculator = ConditionalTurnOrderCalculator.CreateResourceBasedCalculator(
            "health",
            logger: _logger);
        var state = CreateTestCombatStateWithHealth(
            ("hero1", 100f, 100f),
            new[] { ("enemy1", 10f, 50f), ("enemy2", 50f, 50f) }
        );
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        Assert.Equal("enemy1", result.Value[0]); // 20% health
        Assert.Equal("enemy2", result.Value[1]);  // 100% health (comes before "hero1" alphabetically)
        Assert.Equal("hero1", result.Value[2]);   // 100% health
    }
    
    [Fact]
    public void CreateEnemiesFirstCalculator_ShouldOrderEnemiesBeforeHero()
    {
        // Arrange
        var calculator = ConditionalTurnOrderCalculator.CreateEnemiesFirstCalculator(_logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2", "enemy3" });
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        Assert.Equal("enemy1", result.Value[0]);
        Assert.Equal("enemy2", result.Value[1]);
        Assert.Equal("enemy3", result.Value[2]);
        Assert.Equal("hero1", result.Value[3]); // Hero last
    }
    
    [Fact]
    public void CreateHybridCalculator_ShouldPrioritizeLowHealthThenSpeed()
    {
        // Arrange
        var calculator = ConditionalTurnOrderCalculator.CreateHybridCalculator(
            "speed",
            "health",
            0.3f,
            logger: _logger);
        var state = CreateTestCombatStateWithHealthAndSpeed(
            ("hero1", 100f, 100f, 10f),
            new[] 
            { 
                ("enemy1", 50f, 50f, 5f),   // 100% health, speed 5
                ("enemy2", 10f, 50f, 20f),  // 20% health (low), speed 20
                ("enemy3", 15f, 50f, 15f)   // 30% health (low), speed 15
            }
        );
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        // Low health entities first, ordered by speed
        Assert.Equal("enemy2", result.Value[0]); // Low health, speed 20
        Assert.Equal("enemy3", result.Value[1]); // Low health, speed 15
        // Then normal health entities by speed
        Assert.Equal("hero1", result.Value[2]);  // Normal health, speed 10
        Assert.Equal("enemy1", result.Value[3]); // Normal health, speed 5
    }
    
    [Fact]
    public void Strategy_ShouldReturnConditional()
    {
        // Arrange
        var calculator = new ConditionalTurnOrderCalculator(state => new List<string>(), _logger);
        
        // Act & Assert
        Assert.Equal(TurnStrategy.CONDITIONAL, calculator.Strategy);
    }
    
    [Fact]
    public void CalculateTurnOrder_WithExceptionInCustomFunction_ShouldReturnFailure()
    {
        // Arrange
        var calculator = new ConditionalTurnOrderCalculator(
            state => throw new InvalidOperationException("Test exception"),
            _logger
        );
        var state = CreateTestCombatState("hero1", new[] { "enemy1" });
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Test exception", result.Error);
    }
    
    private CombatState CreateTestCombatState(string heroId, string[] enemyIds)
    {
        return TurnOrderTestHelper.CreateTestCombatState(heroId, enemyIds);
    }
    
    private CombatState CreateTestCombatStateWithHealth(
        (string id, float current, float max) heroData,
        (string id, float current, float max)[] enemyData)
    {
        return TurnOrderTestHelper.CreateTestCombatStateWithHealth(heroData, enemyData);
    }
    
    private CombatState CreateTestCombatStateWithHealthAndSpeed(
        (string id, float currentHealth, float maxHealth, float speed) heroData,
        (string id, float currentHealth, float maxHealth, float speed)[] enemyData)
    {
        return TurnOrderTestHelper.CreateTestCombatStateWithHealthAndSpeed(heroData, enemyData);
    }
}
