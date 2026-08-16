using System.ComponentModel.DataAnnotations;

namespace API.Models.StatusEffects;

/// <summary>
/// Request para processar status effects de uma entidade
/// </summary>
public class ProcessStatusEffectsRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    [Required]
    public string TargetId { get; set; } = string.Empty;
    
    /// <summary>
    /// Timing de processamento (START_OF_TURN, END_OF_TURN, etc.)
    /// </summary>
    [Required]
    public string Timing { get; set; } = string.Empty;
    
    /// <summary>
    /// Turno atual
    /// </summary>
    public int CurrentTurn { get; set; }
}
