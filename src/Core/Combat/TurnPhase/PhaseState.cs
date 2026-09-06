namespace Core.Combat.TurnPhase;

/// <summary>
/// Estado atual do sistema de fases dentro de um combate.
/// Imutável - cada transição cria um novo PhaseState.
/// </summary>
public sealed record PhaseState
{
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
    
}
