using Core.Combat.Models;
using Core.Common;

namespace Core.Combat;

/// <summary>
/// Interface para gerenciamento de ações.
/// </summary>
public interface IActionManager
{
    /// <summary>
    /// Carrega definições de ações de um config.
    /// </summary>
    /// <param name="configName">Nome do config</param>
    void LoadActionDefinitions(string configName);
    
    /// <summary>
    /// Obtém definição de uma ação.
    /// </summary>
    /// <param name="actionId">ID da ação</param>
    /// <returns>Definição da ação ou erro</returns>
    Result<ActionDefinition> GetDefinition(string actionId);
    
    /// <summary>
    /// Obtém todas as definições de ações.
    /// </summary>
    IReadOnlyList<ActionDefinition> GetAllDefinitions();
    
    /// <summary>
    /// Obtém definições por tipo.
    /// </summary>
    IReadOnlyList<ActionDefinition> GetDefinitionsByType(ActionType actionType);
    
    /// <summary>
    /// Obtém definições por tag.
    /// </summary>
    IReadOnlyList<ActionDefinition> GetDefinitionsByTag(string tag);
    
    /// <summary>
    /// Valida definição de ação.
    /// </summary>
    Result ValidateActionDefinition(ActionDefinition definition);
}
