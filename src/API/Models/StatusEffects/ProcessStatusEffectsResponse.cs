using Core.StatusEffects;

namespace API.Models.StatusEffects;

/// <summary>
/// Response do processamento de status effects
/// </summary>
public class ProcessStatusEffectsResponse
{
    /// <summary>
    /// ID da entidade processada
    /// </summary>
    public Guid TargetId { get; set; }
    
    /// <summary>
    /// Resultados individuais de cada status effect
    /// </summary>
    public List<StatusEffectTickResponse> TickResults { get; set; } = new();
    
    /// <summary>
    /// Status effects que expiraram
    /// </summary>
    public List<StatusEffectResponse> ExpiredStatus { get; set; } = new();
    
    public static ProcessStatusEffectsResponse FromProcessResult(StatusEffectProcessResult result)
    {
        return new ProcessStatusEffectsResponse
        {
            TargetId = result.TargetId,
            TickResults = result.TickResults.Select(StatusEffectTickResponse.FromTickResult).ToList(),
            ExpiredStatus = result.ExpiredStatus.Select(StatusEffectResponse.FromInstance).ToList()
        };
    }
}

/// <summary>
/// Response de um tick individual de status effect
/// </summary>
public class StatusEffectTickResponse
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
    /// Tipo do status effect
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>
    /// Valor aplicado (dano, cura, etc.)
    /// </summary>
    public float Value { get; set; }
    
    /// <summary>
    /// Se o efeito foi bloqueado
    /// </summary>
    public bool WasBlocked { get; set; }
    
    /// <summary>
    /// Mensagem descritiva
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    public static StatusEffectTickResponse FromTickResult(StatusEffectTickResult result)
    {
        return new StatusEffectTickResponse
        {
            InstanceId = result.InstanceId,
            StatusId = result.StatusId,
            Type = result.Type.ToString(),
            Value = result.Value,
            WasBlocked = result.WasBlocked,
            Message = result.Message
        };
    }
}
