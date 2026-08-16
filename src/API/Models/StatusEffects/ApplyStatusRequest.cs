using System.ComponentModel.DataAnnotations;

namespace API.Models.StatusEffects;

/// <summary>
/// Request para aplicar um status effect a uma entidade
/// </summary>
public class ApplyStatusRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    [Required]
    public string TargetId { get; set; } = string.Empty;
    
    /// <summary>
    /// ID do status effect a aplicar
    /// </summary>
    [Required]
    public string StatusId { get; set; } = string.Empty;
    
    /// <summary>
    /// Número de stacks (padrão: 1)
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Stacks { get; set; } = 1;
    
    /// <summary>
    /// Duração em turnos (null = usar padrão da definição)
    /// </summary>
    public int? Duration { get; set; }
    
    /// <summary>
    /// ID da entidade que aplicou (opcional)
    /// </summary>
    public string? SourceId { get; set; }
}
