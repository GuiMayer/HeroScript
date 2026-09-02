using Core.Combat.TurnPhase;
using Core.Logging;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class PrioritySystemTests
{
    [Fact]
    public void PhaseState_DefensivelyCopiesPriorityState()
    {
        var order = new List<string> { "hero", "enemy" };
        var passed = new Dictionary<string, bool> { ["hero"] = false };
        var phase = new PhaseState
        {
            PriorityOrder = order,
            PlayerPassedPriority = passed
        };

        order.Clear();
        passed["hero"] = true;

        Assert.Equal(new[] { "hero", "enemy" }, phase.PriorityOrder);
        Assert.False(phase.PlayerPassedPriority["hero"]);
        Assert.Equal(DateTime.UnixEpoch, phase.PhaseStartTime);
    }

    private readonly ILogger _logger;
    
    public PrioritySystemTests()
    {
        _logger = new ConsoleLogger(nameof(PrioritySystemTests));
    }

    private static PhaseState CreatePhaseState(params string[] players)
    {
        return new PhaseState
        {
            CurrentPhaseId = "main",
            ActivePlayerId = players.FirstOrDefault() ?? string.Empty,
            PriorityOrder = players.ToList(),
            PlayerPassedPriority = players.ToDictionary(player => player, _ => false)
        };
    }
    
    [Fact]
    public void Initialize_ShouldSetFirstPlayerAsPriority()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        
        // Act
        var state = CreatePhaseState(players.ToArray());
        var combatState = new Core.Combat.Models.CombatState { PhaseState = state };
        var result = prioritySystem.GetPriorityPlayer(combatState);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player1", result.Value);
    }
    
    [Fact]
    public void Initialize_EmptyList_ShouldFail()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string>();
        
        // Act
        var state = CreatePhaseState(players.ToArray());
        
        // Assert
        Assert.Empty(state.ActivePlayerId);
        Assert.Empty(state.PlayerPassedPriority);
    }
    
    [Fact]
    public void PassPriority_ShouldMoveToNextPlayer()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        var state = CreatePhaseState(players.ToArray());
        
        // Act
        var result = prioritySystem.PassPriority(state, "player1");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.PlayerPassedPriority["player1"]);
        Assert.False(result.Value.PlayerPassedPriority["player2"]);
    }
    
    [Fact]
    public void PassPriority_LastPlayer_ShouldWrapToFirst()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2" };
        var state = CreatePhaseState(players.ToArray());
        
        // Act
        var result1 = prioritySystem.PassPriority(state, "player1");
        var result = prioritySystem.PassPriority(result1.Value, "player2");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(prioritySystem.AllPlayersPassedPriority(result.Value));
    }
    
    [Fact]
    public void HasPriority_CurrentPlayer_ShouldReturnTrue()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2" };
        var state = CreatePhaseState(players.ToArray());
        
        // Act & Assert
        Assert.Equal("player1", state.ActivePlayerId);
        Assert.NotEqual("player2", state.ActivePlayerId);
    }
    
    [Fact]
    public void ResetPriority_ShouldReturnToFirstPlayer()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        var state = CreatePhaseState(players.ToArray());
        var passedState = state with
        {
            PlayerPassedPriority = players.ToDictionary(player => player, _ => true)
        };
        
        // Act
        var result = prioritySystem.ResetPriority(passedState, "player1");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player1", result.Value.ActivePlayerId);
        Assert.All(result.Value.PlayerPassedPriority.Values, Assert.False);
    }
    
    [Fact]
    public void SetPriorityPlayer_ValidPlayer_ShouldSetPriority()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        var state = CreatePhaseState(players.ToArray());
        
        // Act
        var result = prioritySystem.ResetPriority(state, "player3");
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("player3", result.Value.ActivePlayerId);
    }
    
    [Fact]
    public void SetPriorityPlayer_InvalidPlayer_ShouldFail()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2" };
        var state = CreatePhaseState(players.ToArray());
        
        // Act
        var result = prioritySystem.PassPriority(state, string.Empty);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("cannot be empty", result.Error);
    }
    
    [Fact]
    public void GetPriorityOrder_ShouldReturnAllPlayers()
    {
        // Arrange
        var prioritySystem = new PrioritySystem(_logger);
        var players = new List<string> { "player1", "player2", "player3" };
        var state = CreatePhaseState(players.ToArray());
        
        // Act
        var order = state.PriorityOrder;
        
        // Assert
        Assert.Equal(3, order.Count);
        Assert.Equal(players, order);
    }
}
