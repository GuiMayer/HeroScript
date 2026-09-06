using Core.Common;

namespace Core.Resources;

/// <summary>
/// Interface para gerenciamento de recursos.
/// </summary>
public interface IResourceManager
{
    /// <summary>
    /// Carrega definições de recursos de um config.
    /// </summary>
    /// <param name="configName">Nome do config</param>
    void LoadResourceDefinitions(string configName);
    
    /// <summary>
    /// Obtém definição de um recurso.
    /// </summary>
    /// <param name="resourceId">ID do recurso</param>
    /// <returns>Definição do recurso ou erro</returns>
    Result<ResourceDefinition> GetDefinition(string resourceId);
    
    /// <summary>
    /// Obtém todas as definições de recursos.
    /// </summary>
    IReadOnlyList<ResourceDefinition> GetAllDefinitions();
    
    /// <summary>
    /// Obtém definições por categoria.
    /// </summary>
    IReadOnlyList<ResourceDefinition> GetDefinitionsByCategory(ResourceCategory category);
    
    /// <summary>
    /// Obtém definições por tag.
    /// </summary>
    IReadOnlyList<ResourceDefinition> GetDefinitionsByTag(string tag);
    
    /// <summary>
    /// Cria um pool de recurso.
    /// </summary>
    /// <param name="resourceId">ID do recurso</param>
    /// <param name="initialCurrent">Valor inicial (opcional)</param>
    /// <returns>Pool criado</returns>
    ResourcePool CreatePool(string resourceId, float? initialCurrent = null);
    
    /// <summary>
    /// Cria pool a partir de definição.
    /// </summary>
    ResourcePool CreatePoolFromDefinition(
        ResourceDefinition definition, 
        float? initialCurrent = null);
    
    /// <summary>
    /// Cria pools padrão para todos os recursos.
    /// </summary>
    Dictionary<string, ResourcePool> CreateDefaultPools();
    
    /// <summary>
    /// Valida se um recurso existe.
    /// </summary>
    bool ValidateResourceExists(string resourceId);
    
    /// <summary>
    /// Valida se há recurso suficiente para um custo.
    /// </summary>
    Result ValidateCost(ResourcePool pool, float cost);
    
    /// <summary>
    /// Valida definição de recurso.
    /// </summary>
    Result ValidateResourceDefinition(ResourceDefinition definition);
    
}

public interface IRevisionedResourceManager
{
    Result<ResourceDefinition> GetDefinition(
        string resourceId,
        string contentRevision,
        string? configName = null);

    Result<ResourcePool> CreatePool(
        string resourceId,
        float? initialCurrent,
        string contentRevision,
        string? configName = null);
}
