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
    
    /// <summary>
    /// Salva uma nova definição de ação.
    /// </summary>
    /// <param name="definition">Definição da ação</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result SaveDefinition(ActionDefinition definition, string configName = "default");
    
    /// <summary>
    /// Atualiza uma definição de ação existente.
    /// </summary>
    /// <param name="actionId">ID da ação</param>
    /// <param name="updatedDefinition">Definição atualizada</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result UpdateDefinition(string actionId, ActionDefinition updatedDefinition, string configName = "default");
    
    /// <summary>
    /// Deleta uma definição de ação.
    /// </summary>
    /// <param name="actionId">ID da ação</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result DeleteDefinition(string actionId, string configName = "default");
}
