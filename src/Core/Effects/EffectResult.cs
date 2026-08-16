using System.Collections.Immutable;

namespace Core.Effects;

/// <summary>
/// Resultado da execução de um efeito.
/// Contém informações sobre o que foi aplicado e quais entidades foram afetadas.
/// Imutável - representa o estado final após execução.
/// </summary>
public record EffectResult
{
    private ImmutableList<string> _affectedEntityIds = [];
    private ImmutableList<string> _statusApplied = [];
    private ImmutableList<string> _statusRemoved = [];
    private ImmutableList<EffectInstance> _chainedEffects = [];
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

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
    public IReadOnlyList<string> AffectedEntityIds
    {
        get => _affectedEntityIds;
        init => _affectedEntityIds = value?.ToImmutableList() ?? [];
    }
    
    // ===== STATUS =====
    
    /// <summary>
    /// IDs dos status aplicados
    /// </summary>
    public IReadOnlyList<string> StatusApplied
    {
        get => _statusApplied;
        init => _statusApplied = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// IDs dos status removidos
    /// </summary>
    public IReadOnlyList<string> StatusRemoved
    {
        get => _statusRemoved;
        init => _statusRemoved = value?.ToImmutableList() ?? [];
    }
    
    // ===== EFEITOS ENCADEADOS =====
    
    /// <summary>
    /// Efeitos encadeados gerados por este efeito
    /// </summary>
    public IReadOnlyList<EffectInstance> ChainedEffects
    {
        get => _chainedEffects;
        init => _chainedEffects = value?.ToImmutableList() ?? [];
    }
    
    // ===== METADATA =====
    
    /// <summary>
    /// Metadata adicional sobre a execução
    /// Ex: "crit_occurred" = true, "damage_mitigated" = 5.0
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
    
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
    private ImmutableList<StatusTickEffect> _effects = [];
    private ImmutableList<string> _expiredStatusIds = [];
    private ImmutableList<string> _removedStatusIds = [];
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>
    /// Efeitos aplicados durante o tick
    /// </summary>
    public IReadOnlyList<StatusTickEffect> Effects
    {
        get => _effects;
        init => _effects = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// IDs dos status que expiraram
    /// </summary>
    public IReadOnlyList<string> ExpiredStatusIds
    {
        get => _expiredStatusIds;
        init => _expiredStatusIds = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// IDs dos status que foram removidos (dispel, etc.)
    /// </summary>
    public IReadOnlyList<string> RemovedStatusIds
    {
        get => _removedStatusIds;
        init => _removedStatusIds = value?.ToImmutableList() ?? [];
    }
    
    /// <summary>
    /// Metadata adicional
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

/// <summary>
/// Efeito individual aplicado durante um tick de status
/// </summary>
public record StatusTickEffect
{
    private ImmutableDictionary<string, object> _metadata =
        ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);

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
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
