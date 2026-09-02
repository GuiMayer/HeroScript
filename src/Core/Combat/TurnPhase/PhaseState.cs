using System.Collections.Immutable;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Estado atual do sistema de fases dentro de um combate.
/// Imutável - cada transição cria um novo PhaseState.
/// </summary>
public record PhaseState
{
    private ImmutableList<string> _priorityOrder = [];
    private ImmutableDictionary<string, bool> _playerPassedPriority =
        ImmutableDictionary<string, bool>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>
    /// Fase atual do turno
    /// </summary>
    public string CurrentPhaseId { get; init; } = string.Empty;
    
    /// <summary>
    /// Índice da fase atual na sequência (0-based)
    /// </summary>
    public int PhaseIndex { get; init; } = 0;
    
    /// <summary>
    /// Sequência de fases configurada
    /// </summary>
    public PhaseSequenceDefinition PhaseSequence { get; init; } = null!;
    
    /// <summary>
    /// Ordem de prioridade dos jogadores
    /// </summary>
    public IReadOnlyList<string> PriorityOrder
    {
        get => _priorityOrder;
        init => _priorityOrder = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Índice do jogador com prioridade atual
    /// </summary>
    public int CurrentPriorityIndex { get; init; } = 0;
    
    /// <summary>
    /// ID do jogador que tem prioridade atualmente
    /// </summary>
    public string ActivePlayerId { get; init; } = "";
    
    /// <summary>
    /// Pilha de ações pendentes
    /// </summary>
    public ActionStack ActionStack { get; init; } = new();
    
    /// <summary>
    /// Se true, a fase pode transicionar para a próxima
    /// </summary>
    public bool CanTransition { get; init; } = true;
    
    /// <summary>
    /// Rastreia quais jogadores já passaram prioridade na fase atual
    /// </summary>
    public IReadOnlyDictionary<string, bool> PlayerPassedPriority
    {
        get => _playerPassedPriority;
        init => _playerPassedPriority = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, bool>.Empty.WithComparers(StringComparer.Ordinal);
    }
    
    /// <summary>
    /// Timestamp de quando a fase atual começou
    /// </summary>
    public DateTime PhaseStartTime { get; init; } = DateTime.UnixEpoch;
    
}
