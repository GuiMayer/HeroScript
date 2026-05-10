namespace API.Models.StatusEffects;

/// <summary>
/// Request para atualizar a duração de um status effect
/// </summary>
public class RefreshDurationRequest
{
    /// <summary>
    /// Nova duração em turnos (-1 = permanente)
    /// </summary>
    public int Duration { get; set; }
}
