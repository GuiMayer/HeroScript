namespace API.Models.Entities;

/// <summary>
/// DTO para representação de uma entidade.
/// </summary>
public class EntityDto
{
    /// <summary>
    /// ID único da entidade.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo da entidade (Hero, Enemy, NPC).
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// ID da definição base.
    /// </summary>
    public string? DefinitionId { get; set; }
    
    /// <summary>
    /// Nome de exibição.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Recursos da entidade.
    /// </summary>
    public Dictionary<string, ResourcePoolDto>? Resources { get; set; }
    
    /// <summary>
    /// Atributos da entidade.
    /// </summary>
    public Dictionary<string, float>? Attributes { get; set; }
    
    /// <summary>
    /// Efeitos de status ativos.
    /// </summary>
    public List<StatusEffectDto>? StatusEffects { get; set; }
}

/// <summary>
/// DTO para pool de recursos.
/// </summary>
public class ResourcePoolDto
{
    public string ResourceId { get; set; } = string.Empty;
    public float Current { get; set; }
    public float Maximum { get; set; }
    public float Minimum { get; set; }
}

/// <summary>
/// DTO para efeito de status.
/// </summary>
public class StatusEffectDto
{
    public string EffectId { get; set; } = string.Empty;
    public int Stacks { get; set; }
    public int? Duration { get; set; }
}
