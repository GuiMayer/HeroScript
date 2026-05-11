using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class ActionStackTests
{
    [Fact]
    public void NewStack_ShouldBeEmpty()
    {
        // Arrange & Act
        var stack = new ActionStack();
        
        // Assert
        Assert.Equal(0, stack.Size);
        Assert.True(stack.IsEmpty);
    }
    
    [Fact]
    public void Push_ShouldAddActionToStack()
    {
        // Arrange
        var stack = new ActionStack();
        var action = new PendingAction
        {
            ActionId = Guid.NewGuid(),
            PlayerId = "player1",
            ActionType = ActionType.BASIC_ATTACK,
            TargetId = "enemy1"
        };
        
        // Act
        var result = stack.Push(action);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, stack.Size);
        Assert.False(stack.IsEmpty);
    }
    
    [Fact]
    public void Push_NullAction_ShouldFail()
    {
        // Arrange
        var stack = new ActionStack();
        
        // Act
        var result = stack.Push(null!);
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(0, stack.Size);
    }
    
    [Fact]
    public void Pop_ShouldReturnLastPushedAction()
    {
        // Arrange
        var stack = new ActionStack();
        var action1 = new PendingAction
        {
            ActionId = Guid.NewGuid(),
            PlayerId = "player1",
            ActionType = ActionType.BASIC_ATTACK
        };
        var action2 = new PendingAction
        {
            ActionId = Guid.NewGuid(),
            PlayerId = "player2",
            ActionType = ActionType.POWER
        };
        
        stack.Push(action1);
        stack.Push(action2);
        
        // Act
        var result = stack.Pop();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(action2.ActionId, result.Value.ActionId);
        Assert.Equal(1, stack.Size);
    }
    
    [Fact]
    public void Pop_EmptyStack_ShouldFail()
    {
        // Arrange
        var stack = new ActionStack();
        
        // Act
        var result = stack.Pop();
        
        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("empty", result.Error.ToLower());
    }
    
    [Fact]
    public void Peek_ShouldReturnTopWithoutRemoving()
    {
        // Arrange
        var stack = new ActionStack();
        var action = new PendingAction
        {
            ActionId = Guid.NewGuid(),
            PlayerId = "player1",
            ActionType = ActionType.BASIC_ATTACK
        };
        
        stack.Push(action);
        
        // Act
        var result = stack.Peek();
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(action.ActionId, result.Value.ActionId);
        Assert.Equal(1, stack.Size); // Size unchanged
    }
    
    [Fact]
    public void Clear_ShouldRemoveAllActions()
    {
        // Arrange
        var stack = new ActionStack();
        stack.Push(new PendingAction { ActionId = Guid.NewGuid(), PlayerId = "p1", ActionType = ActionType.BASIC_ATTACK });
        stack.Push(new PendingAction { ActionId = Guid.NewGuid(), PlayerId = "p2", ActionType = ActionType.POWER });
        stack.Push(new PendingAction { ActionId = Guid.NewGuid(), PlayerId = "p3", ActionType = ActionType.PASS });
        
        // Act
        stack.Clear();
        
        // Assert
        Assert.Equal(0, stack.Size);
        Assert.True(stack.IsEmpty);
    }
    
    [Fact]
    public void GetAll_ShouldReturnActionsInStackOrder()
    {
        // Arrange
        var stack = new ActionStack();
        var action1 = new PendingAction { ActionId = Guid.NewGuid(), PlayerId = "p1", ActionType = ActionType.BASIC_ATTACK };
        var action2 = new PendingAction { ActionId = Guid.NewGuid(), PlayerId = "p2", ActionType = ActionType.POWER };
        var action3 = new PendingAction { ActionId = Guid.NewGuid(), PlayerId = "p3", ActionType = ActionType.PASS };
        
        stack.Push(action1);
        stack.Push(action2);
        stack.Push(action3);
        
        // Act
        var all = stack.GetAll();
        
        // Assert
        Assert.Equal(3, all.Count);
        Assert.Equal(action3.ActionId, all[0].ActionId); // Top of stack
        Assert.Equal(action2.ActionId, all[1].ActionId);
        Assert.Equal(action1.ActionId, all[2].ActionId); // Bottom of stack
    }
}
