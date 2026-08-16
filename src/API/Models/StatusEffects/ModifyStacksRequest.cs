using System.ComponentModel.DataAnnotations;

namespace API.Models.StatusEffects;

/// <summary>
/// Request para modificar stacks de um status effect
/// </summary>
public class ModifyStacksRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    [Required]
    public string TargetId { get; set; } = string.Empty;
    
    /// <summary>
    /// ID da instância do status
    /// </summary>
    public Guid InstanceId { get; set; }
    
    /// <summary>
    /// Número de stacks a adicionar/remover
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Stacks { get; set; }
}
