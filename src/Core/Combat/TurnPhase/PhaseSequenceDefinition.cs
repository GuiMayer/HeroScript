using System.Collections.Immutable;

namespace Core.Combat.TurnPhase;

/// <summary>
/// Define uma sequência completa de fases para um estilo de jogo específico.
/// Configurável via JSON para suportar diferentes TCGs (Magic, Yu-Gi-Oh!, etc.)
/// </summary>
public record PhaseSequenceDefinition
{
    private ImmutableList<TurnPhase> _phases = [];
    private ImmutableDictionary<TurnPhase, PhaseDefinition> _phaseDetails =
        ImmutableDictionary<TurnPhase, PhaseDefinition>.Empty;

    /// <summary>
    /// Nome para exibição (ex: "Magic: The Gathering Standard")
    /// </summary>
    public string Name { get; init; } = "";
    
    /// <summary>
    /// Descrição do estilo de jogo e suas características
    /// </summary>
    public string Description { get; init; } = "";
    
    /// <summary>
    /// Versão da configuração
    /// </summary>
    public string Version { get; init; } = "1.0.0";
    
    /// <summary>
    /// Lista ordenada de fases que compõem um turno completo
    /// </summary>
    public IReadOnlyList<TurnPhase> Phases
    {
        get => _phases;
        init => _phases = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Detalhes de cada fase (regras, ações permitidas, etc.)
    /// </summary>
    public IReadOnlyDictionary<TurnPhase, PhaseDefinition> PhaseDetails
    {
        get => _phaseDetails;
        init => _phaseDetails = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<TurnPhase, PhaseDefinition>.Empty;
    }
    
    /// <summary>
    /// Se true, permite pular fases opcionais (ex: Main 2 em Magic)
    /// </summary>
    public bool AllowPhaseSkipping { get; init; } = false;
}
