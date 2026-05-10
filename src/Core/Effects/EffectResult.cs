namespace Core.Effects;

/// <summary>
/// Resultado da execução de um efeito.
/// Contém informações sobre o que foi aplicado e quais entidades foram afetadas.
/// Imutável - representa o estado final após execução.
/// </summary>
public record EffectResult
{
    /// <summary>
    /// Se o efeito foi executado com sucesso
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Mensagem de erro (se falhou)
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    // ===== VALORES APLICADOS =====
    
    /// <summary>
    /// Valor final aplicado (após fórmulas, modificadores, etc.)
    /// </summary>
    public float? ValueApplied { get; init; }
    
    /// <summary>
    /// Recurso afetado (health, energy, mana, etc.)
    /// </summary>
    public string? ResourceAffected { get; init; }
    
    // ===== ENTIDADES AFETADAS =====
    
    /// <summary>
    /// IDs das entidades afetadas pelo efeito
    /// </summary>
    public List<string> AffectedEntityIds { get; init; } = new();
    
    // ===== STATUS =====
    
    /// <summary>
    /// IDs dos status aplicados
    /// </summary>
    public List<string> StatusApplied { get; init; } = new();
    
    /// <summary>
    /// IDs dos status removidos
    /// </summary>
    public List<string> StatusRemoved { get; init; } = new();
    
    // ===== EFEITOS ENCADEADOS =====
    
    /// <summary>
    /// Efeitos encadeados gerados por este efeito
    /// </summary>
    public List<EffectInstance> ChainedEffects { get; init; } = new();
    
    // ===== METADATA =====
    
    /// <summary>
    /// Metadata adicional sobre a execução
    /// Ex: "crit_occurred" = true, "damage_mitigated" = 5.0
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    // ===== FACTORY METHODS =====
    
    /// <summary>
    /// Cria resultado de sucesso
    /// </summary>
    public static EffectResult CreateSuccess(float? valueApplied = null, string? resourceAffected = null)
    {
        return new EffectResult
        {
            Success = true,
            ValueApplied = valueApplied,
            ResourceAffected = resourceAffected
        };
    }
    
    /// <summary>
    /// Cria resultado de falha
    /// </summary>
    public static EffectResult CreateFailure(string errorMessage)
    {
        return new EffectResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
    
    /// <summary>
    /// Cria resultado de sucesso com entidades afetadas
    /// </summary>
    public static EffectResult CreateWithAffectedEntities(List<string> entityIds, float? valueApplied = null)
    {
        return new EffectResult
        {
            Success = true,
            AffectedEntityIds = entityIds,
            ValueApplied = valueApplied
        };
    }
}

/// <summary>
/// Resultado de processamento de tick de status.
/// Usado pelo StatusManager para retornar efeitos aplicados durante um tick.
/// </summary>
public record StatusTickResult
{
    /// <summary>
    /// Efeitos aplicados durante o tick
    /// </summary>
    public List<StatusTickEffect> Effects { get; init; } = new();
    
    /// <summary>
    /// IDs dos status que expiraram
    /// </summary>
    public List<string> ExpiredStatusIds { get; init; } = new();
    
    /// <summary>
    /// IDs dos status que foram removidos (dispel, etc.)
    /// </summary>
    public List<string> RemovedStatusIds { get; init; } = new();
    
    /// <summary>
    /// Metadata adicional
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Efeito individual aplicado durante um tick de status
/// </summary>
public record StatusTickEffect
{
    /// <summary>
    /// ID da instância do status que gerou o efeito
    /// </summary>
    public string StatusInstanceId { get; init; } = string.Empty;
    
    /// <summary>
    /// ID da entidade alvo
    /// </summary>
    public string TargetEntityId { get; init; } = string.Empty;
    
    /// <summary>
    /// Tipo do efeito aplicado
    /// </summary>
    public EffectType EffectType { get; init; }
    
    /// <summary>
    /// Recurso afetado (se aplicável)
    /// </summary>
    public string? ResourceId { get; init; }
    
    /// <summary>
    /// Valor aplicado
    /// </summary>
    public float Value { get; init; }
    
    /// <summary>
    /// Metadata adicional
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
