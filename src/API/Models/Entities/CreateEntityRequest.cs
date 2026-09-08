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

    public string SettingId { get; set; } = string.Empty;

    public string ContentRevision { get; set; } = string.Empty;
    
    /// <summary>
    /// ID único para a entidade. Obrigatório para que a criação seja reproduzível.
    /// </summary>
    public string? EntityId { get; set; }
    
    /// <summary>
    /// Nome de exibição customizado (opcional, usa o da definição se não fornecido).
    /// </summary>
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Não suportado neste endpoint. Publique uma definição imutável ou aplique um comando de domínio.
    /// </summary>
    public Dictionary<string, float>? InitialResources { get; set; }
}
