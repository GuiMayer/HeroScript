using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Logging;
using Core.Resources;
using Xunit;

namespace Core.Tests.Combat.TurnOrder;

public class SpeedBasedTurnOrderCalculatorTests
{
    private readonly ILogger _logger;
    
    public SpeedBasedTurnOrderCalculatorTests()
    {
        _logger = TurnOrderTestHelper.CreateTestLogger();
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldOrderBySpeed_HighestFirst()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("speed", logger: _logger);
        var state = CreateTestCombatStateWithSpeed(
            ("hero1", 15f),
            new[] { ("enemy1", 5f), ("enemy2", 20f), ("enemy3", 10f) }
        );
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Count);
        Assert.Equal("enemy2", result.Value[0]); // Speed 20
        Assert.Equal("hero1", result.Value[1]);   // Speed 15
        Assert.Equal("enemy3", result.Value[2]);  // Speed 10
        Assert.Equal("enemy1", result.Value[3]);  // Speed 5
    }
    
    [Fact]
    public void CalculateTurnOrder_WithEqualSpeed_ShouldUseEntityIdForTiebreaker()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("speed", logger: _logger);
        var state = CreateTestCombatStateWithSpeed(
            ("hero1", 10f),
            new[] { ("enemy1", 10f), ("enemy2", 10f) }
        );
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
        // All have same speed, so order by ID
        Assert.Equal("enemy1", result.Value[0]);
        Assert.Equal("enemy2", result.Value[1]);
        Assert.Equal("hero1", result.Value[2]);
    }
    
    [Fact]
    public void CalculateTurnOrder_WithoutConfiguredResource_ShouldFail()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("initiative", logger: _logger);
        var state = CreateTestCombatState("hero1", new[] { "enemy1", "enemy2" });
        
        // Act
        var result = calculator.CalculateTurnOrder(state);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("initiative", result.Error);
    }
    
    [Fact]
    public void CalculateTurnOrder_ShouldRecalculate_EachTime()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("speed", logger: _logger);
        var state = CreateTestCombatStateWithSpeed(
            ("hero1", 10f),
            new[] { ("enemy1", 15f) }
        );
        
        // Act - First calculation
        var result1 = calculator.CalculateTurnOrder(state);
        
        // Modify speed
        var updatedState = ModifyEntitySpeed(state, "hero1", 20f);
        
        // Act - Second calculation
        var result2 = calculator.CalculateTurnOrder(updatedState);
        
        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal("enemy1", result1.Value[0]); // enemy1 was faster
        Assert.Equal("hero1", result2.Value[0]);  // hero1 is now faster
    }
    
    [Fact]
    public void Strategy_ShouldReturnSpeedBased()
    {
        // Arrange
        var calculator = new SpeedBasedTurnOrderCalculator("speed", logger: _logger);
        
        // Act & Assert
        Assert.Equal(TurnStrategy.SPEED_BASED, calculator.Strategy);
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
    
    private CombatState ModifyEntitySpeed(CombatState state, string entityId, float newSpeed)
    {
        if (state.GetActor(entityId) is { } actor)
        {
            var speedPool = actor.ResourceState.Resources["speed"];
            var updatedPool = speedPool with { Current = newSpeed };
            var updatedResources = new Dictionary<string, ResourcePool>(actor.ResourceState.Resources)
            {
                ["speed"] = updatedPool
            };
            var updatedResourceState = actor.ResourceState with { Resources = updatedResources };
            return state.ReplaceActor(actor.WithResourceState(updatedResourceState));
        }
        
        return state;
    }
}
