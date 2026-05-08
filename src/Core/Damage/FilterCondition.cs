namespace Core.Damage;

/// <summary>
/// Tipos de filtros para condições de bucket
/// </summary>
public enum FilterType
{
    /// <summary>
    /// Requer que uma tag específica esteja presente
    /// </summary>
    TAG_PRESENT,
    
    /// <summary>
    /// Requer que uma tag específica esteja ausente
    /// </summary>
    TAG_ABSENT,
    
    /// <summary>
    /// Requer que um modifier específico exista
    /// </summary>
    MODIFIER_PRESENT,
    
    /// <summary>
    /// Requer que um modifier esteja acima de um threshold
    /// </summary>
    MODIFIER_ABOVE,
    
    /// <summary>
    /// Requer que um modifier esteja abaixo de um threshold
    /// </summary>
    MODIFIER_BELOW
}

/// <summary>
/// Condição de filtro para determinar se um bucket deve ser executado
/// </summary>
public record FilterCondition
{
    /// <summary>
    /// Tipo de filtro
    /// </summary>
    public FilterType Type { get; init; }
    
    /// <summary>
    /// Parâmetro do filtro (ex: nome da tag ou modifier)
    /// </summary>
    public string Parameter { get; init; } = string.Empty;
    
    /// <summary>
    /// Valor opcional para comparação (usado em MODIFIER_ABOVE/BELOW)
    /// </summary>
    public object? Value { get; init; }
}
