using System.Collections.Immutable;
using Core.Combat.Models;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Representa uma ação pendente na pilha de resolução (stack).
/// Usado em sistemas TCG complexos onde ações podem ser respondidas antes de resolver.
/// </summary>
public record PendingAction
{
    /// <summary>
    /// Identificador único da ação
    /// </summary>
    public Guid ActionId { get; init; } = Guid.Empty;
    
    /// <summary>
    /// Tipo de ação sendo executada
    /// </summary>
    public ActionType Type { get; init; }
    
    /// <summary>
    /// ID da entidade que está executando a ação
    /// </summary>
    public string ActorId { get; init; } = "";
    
    /// <summary>
    /// ID do alvo da ação (se aplicável)
    /// </summary>
    public string? TargetId { get; init; }
    
    /// <summary>
    /// ID do poder/habilidade sendo usado (se aplicável)
    /// </summary>
    public string? PowerId { get; init; }
    
    /// <summary>
    /// Se true, outros jogadores podem responder a esta ação antes dela resolver
    /// Se false, a ação resolve imediatamente sem janela de resposta
    /// </summary>
    public bool CanRespond { get; init; } = true;
    
    /// <summary>
    /// Posição na pilha (0 = topo, resolve primeiro)
    /// </summary>
    public int StackPosition { get; init; }
    
    /// <summary>
    /// Timestamp de quando a ação foi adicionada à pilha
    /// </summary>
    public DateTime AddedAt { get; init; } = DateTime.UnixEpoch;
    
    /// <summary>
    /// Metadados customizados para extensibilidade
    /// Ex: custos pagos, alvos adicionais, modificadores, etc.
    /// </summary>
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
