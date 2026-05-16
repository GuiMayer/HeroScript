using Core.Combat.Models;
using Core.Common;
using Core.Events;
using Core.Logging;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Implementação do gerenciador da pilha de ações pendentes.
/// Controla empilhamento, desempilhamento e resolução de ações em ordem LIFO.
/// </summary>
public class ActionStackManager : IActionStackManager
{
    private readonly ILogger _logger;
    private readonly IEventBus? _eventBus;
    
    public ActionStackManager(ILogger logger, IEventBus? eventBus = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventBus = eventBus;
    }
    
    public Result<ActionStack> PushAction(ActionStack stack, PendingAction action)
    {
        if (action == null)
        {
            return Result<ActionStack>.Failure("Action cannot be null");
        }

        if (stack.IsResolving)
        {
            return Result<ActionStack>.Failure("Cannot push action while stack is resolving");
        }
        
        if (stack.Size >= stack.MaxStackSize)
        {
            return Result<ActionStack>.Failure($"Stack is full (max size: {stack.MaxStackSize})");
        }
        
        // Criar nova pilha com a ação adicionada
        var newStack = new Stack<PendingAction>(stack.Actions.Reverse());
        
        // Atualizar posição da ação
        var actionWithPosition = action with { StackPosition = newStack.Count };
        newStack.Push(actionWithPosition);
        
        var updatedStack = stack with
        {
            Actions = new Stack<PendingAction>(newStack.Reverse())
        };
        
        _logger.LogDebug($"Pushed action {action.Type} by {action.ActorId} to stack (position {actionWithPosition.StackPosition})");
        
        return Result<ActionStack>.Success(updatedStack);
    }
    
    public Result<(ActionStack, PendingAction)> PopAction(ActionStack stack)
    {
        if (stack.IsEmpty)
        {
            return Result<(ActionStack, PendingAction)>.Failure("Cannot pop from empty stack");
        }
        
        if (stack.IsResolving)
        {
            return Result<(ActionStack, PendingAction)>.Failure("Cannot pop while stack is resolving");
        }
        
        // Criar nova pilha sem o topo
        var newStack = new Stack<PendingAction>(stack.Actions.Reverse());
        var poppedAction = newStack.Pop();
        
        var updatedStack = stack with
        {
            Actions = new Stack<PendingAction>(newStack.Reverse())
        };
        
        _logger.LogDebug($"Popped action {poppedAction.Type} by {poppedAction.ActorId} from stack");
        
        return Result<(ActionStack, PendingAction)>.Success((updatedStack, poppedAction));
    }
    
    public Result<ActionStack> ResolveTop(ActionStack stack, CombatState state)
    {
        if (stack.IsEmpty)
        {
            return Result<ActionStack>.Failure("Cannot resolve empty stack");
        }
        
        // Obter ação do topo
        var topAction = stack.Actions.Peek();
        
        // Marcar como resolvendo
        var resolvingStack = stack with
        {
            IsResolving = true,
            CurrentlyResolving = topAction
        };
        
        _logger.LogDebug($"Resolving action {topAction.Type} by {topAction.ActorId}");
        
        // NOTA: A resolução real da ação deve ser feita pelo CombatSystem
        // Este método apenas gerencia o estado da pilha
        // O CombatSystem deve chamar PopAction após executar a ação
        
        // Por enquanto, apenas removemos a ação da pilha
        var popResult = PopAction(stack);
        if (popResult.IsFailure)
        {
            return Result<ActionStack>.Failure($"Failed to pop action: {popResult.Error}");
        }
        
        var (newStack, _) = popResult.Value;
        
        // Resetar estado de resolução
        var finalStack = newStack with
        {
            IsResolving = false,
            CurrentlyResolving = null
        };
        
        _logger.LogDebug($"Action resolved, stack size now: {finalStack.Size}");
        
        return Result<ActionStack>.Success(finalStack);
    }
    
    public Result<ActionStack> ResolveAll(ActionStack stack, CombatState state)
    {
        var currentStack = stack;
        
        while (!currentStack.IsEmpty)
        {
            var resolveResult = ResolveTop(currentStack, state);
            if (resolveResult.IsFailure)
            {
                return Result<ActionStack>.Failure($"Failed to resolve stack: {resolveResult.Error}");
            }
            
            currentStack = resolveResult.Value;
        }
        
        _logger.LogDebug("All actions resolved, stack is now empty");
        
        return Result<ActionStack>.Success(currentStack);
    }
    
    public bool IsEmpty(ActionStack stack)
    {
        return stack.IsEmpty;
    }
    
    public int GetStackSize(ActionStack stack)
    {
        return stack.Size;
    }
    
    public Result<PendingAction> PeekTop(ActionStack stack)
    {
        if (stack.IsEmpty)
        {
            return Result<PendingAction>.Failure("Stack is empty");
        }
        
        return Result<PendingAction>.Success(stack.Actions.Peek());
    }
}
