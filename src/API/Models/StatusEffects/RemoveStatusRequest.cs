namespace API.Models.StatusEffects;

/// <summary>
/// Request para remover um status effect específico
/// </summary>
public class RemoveStatusRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    public Guid TargetId { get; set; }
    
    /// <summary>
    /// ID da instância do status a remover
    /// </summary>
    public Guid InstanceId { get; set; }
}
