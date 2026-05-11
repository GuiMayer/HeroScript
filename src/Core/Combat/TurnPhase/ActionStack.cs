namespace Core.Combat.TurnPhase;

/// <summary>
/// Pilha de ações pendentes para resolução (LIFO - Last In, First Out).
/// Usado em sistemas TCG complexos onde ações podem ser empilhadas e respondidas.
/// Exemplo: Em Magic, quando um jogador joga um feitiço, o oponente pode responder
/// com outro feitiço antes do primeiro resolver. A pilha resolve de cima para baixo.
/// </summary>
public record ActionStack
{
    /// <summary>
    /// Pilha de ações pendentes (topo = próxima a resolver)
    /// </summary>
    public Stack<PendingAction> Actions { get; init; } = new();
    
    /// <summary>
    /// Se true, a pilha está atualmente resolvendo ações
    /// Durante resolução, novas ações não podem ser adicionadas
    /// </summary>
    public bool IsResolving { get; init; } = false;
    
    /// <summary>
    /// Ação que está sendo resolvida no momento (se IsResolving = true)
    /// </summary>
    public PendingAction? CurrentlyResolving { get; init; }
    
    /// <summary>
    /// Tamanho máximo permitido para a pilha
    /// Previne loops infinitos ou abuso de empilhamento
    /// </summary>
    public int MaxStackSize { get; init; } = 100;
    
    /// <summary>
    /// Helper: verifica se a pilha está vazia
    /// </summary>
    public bool IsEmpty => Actions.Count == 0;
    
    /// <summary>
    /// Helper: obtém o tamanho atual da pilha
    /// </summary>
    public int Size => Actions.Count;
}
