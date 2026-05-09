using Core.Common;

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
    Result<OperationMetadata> GetOperation(string name);

    /// <summary>
    /// Obtém operações agrupadas por categoria
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<OperationMetadata>> GetOperationsByCategory();
}
