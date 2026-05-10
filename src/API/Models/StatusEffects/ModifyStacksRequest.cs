namespace API.Models.StatusEffects;

/// <summary>
/// Request para modificar stacks de um status effect
/// </summary>
public class ModifyStacksRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    public Guid TargetId { get; set; }
    
    /// <summary>
    /// ID da instância do status
    /// </summary>
    public Guid InstanceId { get; set; }
    
    /// <summary>
    /// Número de stacks a adicionar/remover
    /// </summary>
    public int Stacks { get; set; }
}
