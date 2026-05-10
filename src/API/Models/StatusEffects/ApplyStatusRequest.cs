namespace API.Models.StatusEffects;

/// <summary>
/// Request para aplicar um status effect a uma entidade
/// </summary>
public class ApplyStatusRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    public Guid TargetId { get; set; }
    
    /// <summary>
    /// ID do status effect a aplicar
    /// </summary>
    public string StatusId { get; set; } = string.Empty;
    
    /// <summary>
    /// Número de stacks (padrão: 1)
    /// </summary>
    public int Stacks { get; set; } = 1;
    
    /// <summary>
    /// Duração em turnos (null = usar padrão da definição)
    /// </summary>
    public int? Duration { get; set; }
    
    /// <summary>
    /// ID da entidade que aplicou (opcional)
    /// </summary>
    public Guid? SourceId { get; set; }
}
