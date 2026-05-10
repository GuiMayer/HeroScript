namespace API.Models.Entities;

/// <summary>
/// Request para criar uma nova entidade a partir de uma definição.
/// </summary>
public class CreateEntityRequest
{
    /// <summary>
    /// ID da definição de entidade a ser usada.
    /// </summary>
    public string DefinitionId { get; set; } = string.Empty;
    
    /// <summary>
    /// ID único para a entidade (opcional, será gerado se não fornecido).
    /// </summary>
    public string? EntityId { get; set; }
    
    /// <summary>
    /// Nome de exibição customizado (opcional, usa o da definição se não fornecido).
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Valores iniciais de recursos (opcional, usa os padrões da definição se não fornecido).
    /// </summary>
    public Dictionary<string, float>? InitialResources { get; set; }
}
