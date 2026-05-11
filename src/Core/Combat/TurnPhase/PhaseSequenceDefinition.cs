namespace Core.Combat.TurnPhase;

/// <summary>
/// Define uma sequência completa de fases para um estilo de jogo específico.
/// Configurável via JSON para suportar diferentes TCGs (Magic, Yu-Gi-Oh!, etc.)
/// </summary>
public record PhaseSequenceDefinition
{
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
    public List<TurnPhase> Phases { get; init; } = new();
    
    /// <summary>
    /// Detalhes de cada fase (regras, ações permitidas, etc.)
    /// </summary>
    public Dictionary<TurnPhase, PhaseDefinition> PhaseDetails { get; init; } = new();
    
    /// <summary>
    /// Se true, permite pular fases opcionais (ex: Main 2 em Magic)
    /// </summary>
    public bool AllowPhaseSkipping { get; init; } = false;
}
