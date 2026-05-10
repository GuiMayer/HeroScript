namespace API.Models.Entities;

/// <summary>
/// DTO para definição de entidade.
/// </summary>
public class EntityDefinitionDto
{
    /// <summary>
    /// ID único da definição.
    /// </summary>
    public string DefinitionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo da entidade.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Nome de exibição.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Descrição da entidade.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Recursos da entidade.
    /// </summary>
    public Dictionary<string, ResourceDefinitionDto>? Resources { get; set; }
    
    /// <summary>
    /// Atributos da entidade.
    /// </summary>
    public Dictionary<string, float>? Attributes { get; set; }
}

/// <summary>
/// DTO para definição de recurso em uma entidade.
/// </summary>
public class ResourceDefinitionDto
{
    public float Current { get; set; }
    public float Max { get; set; }
}
