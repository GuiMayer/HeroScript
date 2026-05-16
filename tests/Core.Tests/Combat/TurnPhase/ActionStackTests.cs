using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Xunit;

namespace Core.Tests.Combat.TurnPhase;

public class ActionStackTests
{
    private readonly ActionStackManager _manager = new(new Core.Logging.ConsoleLogger(nameof(ActionStackTests)));

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
            ActorId = "player1",
            Type = ActionType.BASIC_ATTACK,
            TargetId = "enemy1"
        };
        
        // Act
        var result = _manager.PushAction(stack, action);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Size);
        Assert.False(result.Value.IsEmpty);
    }
    
    [Fact]
    public void Push_NullAction_ShouldFail()
    {
        // Arrange
        var stack = new ActionStack();
        
        // Act
        var result = _manager.PushAction(stack, null!);
        
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
            ActorId = "player1",
            Type = ActionType.BASIC_ATTACK
        };
        var action2 = new PendingAction
        {
            ActionId = Guid.NewGuid(),
            ActorId = "player2",
            Type = ActionType.POWER
        };
        
        stack = _manager.PushAction(stack, action1).Value;
        stack = _manager.PushAction(stack, action2).Value;
        
        // Act
        var result = _manager.PopAction(stack);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(action2.ActionId, result.Value.Item2.ActionId);
        Assert.Equal(1, result.Value.Item1.Size);
    }
    
    [Fact]
    public void Pop_EmptyStack_ShouldFail()
    {
        // Arrange
        var stack = new ActionStack();
        
        // Act
        var result = _manager.PopAction(stack);
        
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
            ActorId = "player1",
            Type = ActionType.BASIC_ATTACK
        };
        
        stack = _manager.PushAction(stack, action).Value;
        
        // Act
        var result = _manager.PeekTop(stack);
        
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
        stack = _manager.PushAction(stack, new PendingAction { ActionId = Guid.NewGuid(), ActorId = "p1", Type = ActionType.BASIC_ATTACK }).Value;
        stack = _manager.PushAction(stack, new PendingAction { ActionId = Guid.NewGuid(), ActorId = "p2", Type = ActionType.POWER }).Value;
        stack = _manager.PushAction(stack, new PendingAction { ActionId = Guid.NewGuid(), ActorId = "p3", Type = ActionType.PASS }).Value;
        
        // Act
        stack = stack with { Actions = new Stack<PendingAction>() };
        
        // Assert
        Assert.Equal(0, stack.Size);
        Assert.True(stack.IsEmpty);
    }
    
    [Fact]
    public void GetAll_ShouldReturnActionsInStackOrder()
    {
        // Arrange
        var stack = new ActionStack();
        var action1 = new PendingAction { ActionId = Guid.NewGuid(), ActorId = "p1", Type = ActionType.BASIC_ATTACK };
        var action2 = new PendingAction { ActionId = Guid.NewGuid(), ActorId = "p2", Type = ActionType.POWER };
        var action3 = new PendingAction { ActionId = Guid.NewGuid(), ActorId = "p3", Type = ActionType.PASS };
        
        stack = _manager.PushAction(stack, action1).Value;
        stack = _manager.PushAction(stack, action2).Value;
        stack = _manager.PushAction(stack, action3).Value;
        
        // Act
        var all = stack.Actions.ToList();
        
        // Assert
        Assert.Equal(3, all.Count);
        Assert.Equal(action3.ActionId, all[0].ActionId); // Top of stack
        Assert.Equal(action2.ActionId, all[1].ActionId);
        Assert.Equal(action1.ActionId, all[2].ActionId); // Bottom of stack
    }
}
