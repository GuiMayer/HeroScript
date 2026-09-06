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
    /// Substitui AmountPerTurn. Em combate usa o contexto canônico
    /// source.resources, target.resources, owner.resources e run.resources.
    /// </summary>
    public string? Formula { get; init; }
    
    /// <summary>
    /// Quando a regeneração ocorre.
    /// </summary>
    public RegenerationTiming Timing { get; init; }
    public string CalculationChannel { get; init; } = "effect_amount";
    public string? CalculationPipelineId { get; init; }

    /// <summary>Authoring shorthand only; the executor cannot distinguish regeneration from any other resource effect.</summary>
    public Effects.EffectDefinition ToEffect(string resourceId) => new()
    {
        EffectId = $"resource:{resourceId}:regeneration",
        Type = Effects.EffectType.MODIFY_RESOURCE,
        Target = Effects.EffectTarget.SELF,
        TargetResource = resourceId,
        Operation = Effects.ResourceEffectOperation.ADD,
        FlatValue = string.IsNullOrWhiteSpace(Formula) ? AmountPerTurn : null,
        FormulaValue = Formula,
        CalculationChannel = CalculationChannel,
        CalculationPipelineId = CalculationPipelineId,
        Tags = ["regeneration"]
    };
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
