using Core.Combat.TurnPhase;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class PrioritySystemTests
{
    private readonly ILogger _logger;
    
    public PrioritySystemTests()
    {
        _logger = new ConsoleLogger();
    }
    
    [Fact]
    public void Initialize_ShouldSetFirstPlayerAsPriority()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        
        // Act
        var result = prioritySystem.Initialize(players);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player1", prioritySystem.GetCurrentPriorityPlayer());
    }
    
    [Fact]
    public void Initialize_EmptyList_ShouldFail()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string>();
        
        // Act
        var result = prioritySystem.Initialize(players);
        
        // Assert
        Assert.True(result.IsFailure);
    }
    
    [Fact]
    public void PassPriority_ShouldMoveToNextPlayer()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        prioritySystem.Initialize(players);
        
        // Act
        var result = prioritySystem.PassPriority();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player2", prioritySystem.GetCurrentPriorityPlayer());
    }
    
    [Fact]
    public void PassPriority_LastPlayer_ShouldWrapToFirst()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2" };
        prioritySystem.Initialize(players);
        
        // Act
        prioritySystem.PassPriority(); // player2
        var result = prioritySystem.PassPriority(); // wrap to player1
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player1", prioritySystem.GetCurrentPriorityPlayer());
    }
    
    [Fact]
    public void HasPriority_CurrentPlayer_ShouldReturnTrue()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2" };
        prioritySystem.Initialize(players);
        
        // Act & Assert
        Assert.True(prioritySystem.HasPriority("player1"));
        Assert.False(prioritySystem.HasPriority("player2"));
    }
    
    [Fact]
    public void ResetPriority_ShouldReturnToFirstPlayer()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        prioritySystem.Initialize(players);
        prioritySystem.PassPriority(); // player2
        prioritySystem.PassPriority(); // player3
        
        // Act
        var result = prioritySystem.ResetPriority();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player1", prioritySystem.GetCurrentPriorityPlayer());
    }
    
    [Fact]
    public void SetPriorityPlayer_ValidPlayer_ShouldSetPriority()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        prioritySystem.Initialize(players);
        
        // Act
        var result = prioritySystem.SetPriorityPlayer("player3");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player3", prioritySystem.GetCurrentPriorityPlayer());
    }
    
    [Fact]
    public void SetPriorityPlayer_InvalidPlayer_ShouldFail()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2" };
        prioritySystem.Initialize(players);
        
        // Act
        var result = prioritySystem.SetPriorityPlayer("player999");
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("not in priority order", result.Error);
    }
    
    [Fact]
    public void GetPriorityOrder_ShouldReturnAllPlayers()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        prioritySystem.Initialize(players);
        
        // Act
        var order = prioritySystem.GetPriorityOrder();
        
        // Assert
        Assert.Equal(3, order.Count);
        Assert.Equal(players, order);
    }
}
