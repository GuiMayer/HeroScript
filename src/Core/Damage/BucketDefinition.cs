namespace Core.Damage;

/// <summary>
/// Definição de um bucket do pipeline de dano (carregado de JSON)
/// </summary>
public record BucketDefinition
{
    /// <summary>
    /// Identificador único do bucket
    /// </summary>
    public string BucketId { get; init; } = string.Empty;
    
    /// <summary>
    /// Ordem de execução (1, 2, 3, ...)
    /// </summary>
    public int Order { get; init; }
    
    /// <summary>
    /// Condições que devem ser satisfeitas para executar o bucket
    /// </summary>
    public List<FilterCondition> FilterConditions { get; init; } = new();
    
    /// <summary>
    /// Operações a serem executadas sequencialmente
    /// </summary>
    public List<BucketOperation> Operations { get; init; } = new();
    
    /// <summary>
    /// Se true, emite BucketProcessedEvent após execução
    /// </summary>
    public bool EmitEvents { get; init; } = true;
}
