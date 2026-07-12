using Core.Common;
using System.Text.Json;

namespace Core.Resources;

/// <summary>
/// Interface para persistência de definições de recursos (Actions, Entities, Status, Gambits).
/// Permite salvar, atualizar e deletar arquivos JSON de definições.
/// </summary>
public interface IDefinitionPersister
{
    /// <summary>
    /// Salva uma nova definição de recurso.
    /// </summary>
    /// <param name="resourceType">Tipo do recurso (ex: "actions", "entities", "status", "gambits")</param>
    /// <param name="resourceId">ID único do recurso</param>
    /// <param name="definition">Documento JSON com a definição</param>
    /// <param name="configName">Nome da configuração (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result SaveDefinition(string resourceType, string resourceId, JsonDocument definition, string configName = "default");
    
    /// <summary>
    /// Atualiza uma definição de recurso existente.
    /// </summary>
    /// <param name="resourceType">Tipo do recurso</param>
    /// <param name="resourceId">ID único do recurso</param>
    /// <param name="definition">Documento JSON atualizado</param>
    /// <param name="configName">Nome da configuração (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result UpdateDefinition(string resourceType, string resourceId, JsonDocument definition, string configName = "default");
    
    /// <summary>
    /// Deleta uma definição de recurso.
    /// </summary>
    /// <param name="resourceType">Tipo do recurso</param>
    /// <param name="resourceId">ID único do recurso</param>
    /// <param name="configName">Nome da configuração (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result DeleteDefinition(string resourceType, string resourceId, string configName = "default");
    
    /// <summary>
    /// Verifica se uma definição existe.
    /// </summary>
    /// <param name="resourceType">Tipo do recurso</param>
    /// <param name="resourceId">ID único do recurso</param>
    /// <param name="configName">Nome da configuração (padrão: "default")</param>
    /// <returns>True se a definição existe</returns>
    bool DefinitionExists(string resourceType, string resourceId, string configName = "default");
}
