namespace API.Models.StatusEffects;

/// <summary>
/// Request para processar status effects de uma entidade
/// </summary>
public class ProcessStatusEffectsRequest
{
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    public Guid TargetId { get; set; }
    
    /// <summary>
    /// Timing de processamento (START_OF_TURN, END_OF_TURN, etc.)
    /// </summary>
    public string Timing { get; set; } = string.Empty;
    
    /// <summary>
    /// Turno atual
    /// </summary>
    public int CurrentTurn { get; set; }
}
