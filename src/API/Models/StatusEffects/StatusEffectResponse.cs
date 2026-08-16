using Core.StatusEffects;

namespace API.Models.StatusEffects;

/// <summary>
/// Response contendo informações de um status effect
/// </summary>
public class StatusEffectResponse
{
    /// <summary>
    /// ID da instância do status
    /// </summary>
    public Guid InstanceId { get; set; }
    
    /// <summary>
    /// ID do status effect
    /// </summary>
    public string StatusId { get; set; } = string.Empty;
    
    /// <summary>
    /// Nome para exibição
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Tipo do status effect
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Número de stacks
    /// </summary>
    public int Stacks { get; set; }
    
    /// <summary>
    /// Duração restante em turnos
    /// </summary>
    public int Duration { get; set; }
    
    /// <summary>
    /// ID da entidade que aplicou
    /// </summary>
    public string? SourceId { get; set; }
    
    /// <summary>
    /// Turno em que foi aplicado
    /// </summary>
    public int TurnApplied { get; set; }
    
    public static StatusEffectResponse FromInstance(StatusEffectInstance instance)
    {
        return new StatusEffectResponse
        {
            InstanceId = instance.InstanceId,
            StatusId = instance.Definition.StatusId,
            DisplayName = instance.Definition.DisplayName,
            Type = instance.Definition.Type.ToString(),
            Stacks = instance.Stacks,
            Duration = instance.Duration,
            SourceId = instance.SourceId,
            TurnApplied = instance.TurnApplied
        };
    }
}
