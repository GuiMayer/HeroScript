using System.Collections.Immutable;

namespace Core.Damage;

/// <summary>
/// Definição de um bucket do pipeline de dano (carregado de JSON).
/// Para exemplos e guia completo, veja: src/Core/Damage/README.md
/// </summary>
public record BucketDefinition
{
    private ImmutableArray<FilterCondition> _filterConditions = ImmutableArray<FilterCondition>.Empty;
    private ImmutableArray<BucketOperation> _operations = ImmutableArray<BucketOperation>.Empty;

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
    public IReadOnlyList<FilterCondition> FilterConditions
    {
        get => _filterConditions;
        init => _filterConditions = value?.ToImmutableArray() ?? ImmutableArray<FilterCondition>.Empty;
    }
    
    /// <summary>
    /// Operações a serem executadas sequencialmente
    /// </summary>
    public IReadOnlyList<BucketOperation> Operations
    {
        get => _operations;
        init => _operations = value?.ToImmutableArray() ?? ImmutableArray<BucketOperation>.Empty;
    }
    
    /// <summary>
    /// Se true, emite BucketProcessedEvent após execução
    /// </summary>
    public bool EmitEvents { get; init; } = true;
}
