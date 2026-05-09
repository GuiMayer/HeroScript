namespace Core.Damage;

/// <summary>
/// Tipos de operações que podem ser executadas em um bucket
/// </summary>
public enum OperationType
{
    /// <summary>
    /// Adiciona valor flat ao dano atual
    /// </summary>
    ADD_FLAT,
    
    /// <summary>
    /// Multiplica dano atual por valor
    /// </summary>
    MULTIPLY,
    
    /// <summary>
    /// Aplica fórmula do MathEngine
    /// </summary>
    APPLY_FORMULA,
    
    /// <summary>
    /// Adiciona tag ao contexto
    /// </summary>
    SET_TAG,
    
    /// <summary>
    /// Remove tag do contexto
    /// </summary>
    REMOVE_TAG,
    
    /// <summary>
    /// Define valor de modifier
    /// </summary>
    SET_MODIFIER,
    
    /// <summary>
    /// Adiciona valor a modifier existente
    /// </summary>
    ADD_TO_MODIFIER,
    
    /// <summary>
    /// Operação especial: rola tier de crítico multi-tier
    /// </summary>
    ROLL_CRIT_TIER,
    
    /// <summary>
    /// Adiciona multiplicador "more" à lista (não aplica imediatamente)
    /// </summary>
    ADD_MORE_MULTIPLIER,
    
    /// <summary>
    /// Aplica todos os multiplicadores "more" acumulados sequencialmente
    /// </summary>
    APPLY_MORE_MULTIPLIERS
}

/// <summary>
/// Operação a ser executada em um bucket.
/// Para lista completa de tipos e exemplos, veja: src/Core/Damage/README.md
/// </summary>
public record BucketOperation
{
    /// <summary>
    /// Tipo de operação
    /// </summary>
    public OperationType Type { get; init; }
    
    /// <summary>
    /// Fonte do valor (ex: "modifier:crit_chance", "formula:ARMOR_REDUCTION")
    /// </summary>
    public string Source { get; init; } = string.Empty;
    
    /// <summary>
    /// Parâmetros adicionais para a operação
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = new();
}
