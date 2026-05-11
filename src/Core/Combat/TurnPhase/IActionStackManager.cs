using Core.Combat.Models;
using Core.Common;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Gerenciador da pilha de ações pendentes (Action Stack).
/// Controla empilhamento, desempilhamento e resolução de ações em ordem LIFO.
/// Usado em sistemas TCG complexos onde ações podem ser respondidas antes de resolver.
/// </summary>
public interface IActionStackManager
{
    /// <summary>
    /// Adiciona uma ação ao topo da pilha.
    /// </summary>
    /// <param name="stack">Pilha atual</param>
    /// <param name="action">Ação a ser adicionada</param>
    /// <returns>Nova pilha com a ação adicionada</returns>
    Result<ActionStack> PushAction(ActionStack stack, PendingAction action);
    
    /// <summary>
    /// Remove e retorna a ação do topo da pilha.
    /// </summary>
    /// <param name="stack">Pilha atual</param>
    /// <returns>Nova pilha e a ação removida</returns>
    Result<(ActionStack, PendingAction)> PopAction(ActionStack stack);
    
    /// <summary>
    /// Resolve a ação do topo da pilha.
    /// A ação é removida da pilha e executada no contexto do combate.
    /// </summary>
    /// <param name="stack">Pilha atual</param>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Nova pilha após resolução</returns>
    Result<ActionStack> ResolveTop(ActionStack stack, CombatState state);
    
    /// <summary>
    /// Resolve todas as ações na pilha em ordem LIFO.
    /// Continua até a pilha estar vazia.
    /// </summary>
    /// <param name="stack">Pilha atual</param>
    /// <param name="state">Estado atual do combate</param>
    /// <returns>Pilha vazia após todas as resoluções</returns>
    Result<ActionStack> ResolveAll(ActionStack stack, CombatState state);
    
    /// <summary>
    /// Verifica se a pilha está vazia.
    /// </summary>
    /// <param name="stack">Pilha a verificar</param>
    /// <returns>True se vazia</returns>
    bool IsEmpty(ActionStack stack);
    
    /// <summary>
    /// Obtém o tamanho atual da pilha.
    /// </summary>
    /// <param name="stack">Pilha a verificar</param>
    /// <returns>Número de ações na pilha</returns>
    int GetStackSize(ActionStack stack);
    
    /// <summary>
    /// Obtém a ação do topo da pilha sem removê-la.
    /// </summary>
    /// <param name="stack">Pilha a verificar</param>
    /// <returns>Ação do topo, ou erro se pilha vazia</returns>
    Result<PendingAction> PeekTop(ActionStack stack);
}
