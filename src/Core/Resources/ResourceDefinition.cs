using System.Collections.Immutable;

namespace Core.Resources;

/// <summary>
/// Definição de um recurso (metadados carregados de JSON).
/// Recursos são valores numéricos como HP, energia, mana, stamina, etc.
/// </summary>
public record ResourceDefinition
{
    private ImmutableArray<string> _tags = ImmutableArray<string>.Empty;
    private ImmutableArray<ResourceThresholdPolicy> _thresholdPolicies = [];

    /// <summary>
    /// Identificador único do recurso (ex: "health", "energy", "mana").
    /// </summary>
    public string ResourceId { get; init; } = string.Empty;
    
    /// <summary>
    /// Nome de exibição completo (ex: "Health Points").
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;
    
    /// <summary>
    /// Nome curto para UI (ex: "HP").
    /// </summary>
    public string ShortName { get; init; } = string.Empty;
    
    /// <summary>
    /// Categoria do recurso.
    /// </summary>
    public ResourceCategory Category { get; init; }
    
    /// <summary>
    /// Valor mínimo padrão.
    /// </summary>
    public float DefaultMin { get; init; }
    
    /// <summary>
    /// Valor máximo padrão.
    /// </summary>
    public float DefaultMax { get; init; }
    
    /// <summary>
    /// Valor inicial padrão.
    /// </summary>
    public float DefaultCurrent { get; init; }
    
    /// <summary>
    /// Se o recurso pode ter valores negativos.
    /// Útil para dívidas, penalidades, etc.
    /// </summary>
    public bool CanBeNegative { get; init; }
    
    /// <summary>
    /// Se o recurso pode ultrapassar o máximo.
    /// Útil para shields temporários, overcharge, etc.
    /// </summary>
    public bool CanExceedMax { get; init; }
    
    /// <summary>
    /// Configuração de regeneração (opcional).
    /// </summary>
    public RegenerationConfig? Regeneration { get; init; }
    
    /// <summary>
    /// Multiplicador de custo para este recurso.
    /// </summary>
    public float CostMultiplier { get; init; } = 1.0f;
    
    /// <summary>
    /// Tags para categorização e busca.
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? ImmutableArray<string>.Empty;
    }

    /// <summary>
    /// Consequences owned by this resource when it reaches a configured bound.
    /// Neither ResourceId nor Category implies defeat or any other outcome.
    /// </summary>
    public IReadOnlyList<ResourceThresholdPolicy> ThresholdPolicies
    {
        get => _thresholdPolicies;
        init => _thresholdPolicies = value?.ToImmutableArray() ?? [];
    }
}
