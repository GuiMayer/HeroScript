namespace API.Models.Entities;

/// <summary>
/// Request para atualizar uma entidade existente.
/// </summary>
public class UpdateEntityRequest
{
    /// <summary>
    /// Novo nome de exibição (opcional).
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Novos valores de recursos (opcional).
    /// </summary>
    public Dictionary<string, float>? Resources { get; set; }
    
    /// <summary>
    /// Novos valores de atributos (opcional).
    /// </summary>
    public Dictionary<string, float>? Attributes { get; set; }
}
