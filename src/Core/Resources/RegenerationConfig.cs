namespace Core.Resources;

/// <summary>
/// Configuração de regeneração de um recurso.
/// </summary>
public record RegenerationConfig
{
    /// <summary>
    /// Se a regeneração está habilitada.
    /// </summary>
    public bool Enabled { get; init; }
    
    /// <summary>
    /// Quantidade fixa regenerada por turno.
    /// </summary>
    public float AmountPerTurn { get; init; }
    
    /// <summary>
    /// Fórmula dinâmica para calcular regeneração (opcional).
    /// Contexto disponível: current, max, percent
    /// </summary>
    public string? Formula { get; init; }
    
    /// <summary>
    /// Quando a regeneração ocorre.
    /// </summary>
    public RegenerationTiming Timing { get; init; }
}

/// <summary>
/// Momento em que a regeneração ocorre.
/// </summary>
public enum RegenerationTiming
{
    /// <summary>
    /// Regenera no início do turno.
    /// </summary>
    START_TURN,
    
    /// <summary>
    /// Regenera no fim do turno.
    /// </summary>
    END_TURN,
    
    /// <summary>
    /// Regenera apenas fora de combate.
    /// </summary>
    OUT_OF_COMBAT
}
