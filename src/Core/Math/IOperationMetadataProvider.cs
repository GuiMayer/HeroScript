namespace Core.Math;

/// <summary>
/// Interface para provedor de metadados de operações matemáticas
/// </summary>
public interface IOperationMetadataProvider
{
    /// <summary>
    /// Obtém todas as operações disponíveis
    /// </summary>
    IReadOnlyList<OperationMetadata> GetAllOperations();

    /// <summary>
    /// Obtém metadados de uma operação específica
    /// </summary>
    OperationMetadata? GetOperation(string name);

    /// <summary>
    /// Obtém operações agrupadas por categoria
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<OperationMetadata>> GetOperationsByCategory();
}

/// <summary>
/// Metadados de uma operação matemática
/// </summary>
public class OperationMetadata
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int MinValues { get; set; }
    public int MaxValues { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Behavior { get; set; } = string.Empty;
    public bool IsUnary { get; set; }
}
