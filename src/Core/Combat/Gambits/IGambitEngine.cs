using Core.Common;
using Core.Entity;
using Core.Entity.Controllers;

namespace Core.Combat.Gambits;

public interface IGambitEngine
{
    Result LoadDefinitions(string configName);
    Result<GambitDefinition> GetDefinition(string gambitId);
    IReadOnlyList<GambitDefinition> GetAllDefinitions();
    Result<EntityAction> DecideAction(Entity.Entity controlledEntity, Models.CombatState combatState, IEnumerable<string>? gambitIds = null);
    Result<GambitDecision> DecideActionWithMetadata(Entity.Entity controlledEntity, Models.CombatState combatState, IEnumerable<string>? gambitIds = null);
    
    /// <summary>
    /// Salva uma nova definição de gambit.
    /// </summary>
    /// <param name="definition">Definição do gambit</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result SaveDefinition(GambitDefinition definition, string configName = "default");
    
    /// <summary>
    /// Atualiza uma definição de gambit existente.
    /// </summary>
    /// <param name="gambitId">ID do gambit</param>
    /// <param name="updatedDefinition">Definição atualizada</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result UpdateDefinition(string gambitId, GambitDefinition updatedDefinition, string configName = "default");
    
    /// <summary>
    /// Deleta uma definição de gambit.
    /// </summary>
    /// <param name="gambitId">ID do gambit</param>
    /// <param name="configName">Nome do config (padrão: "default")</param>
    /// <returns>Result indicando sucesso ou falha</returns>
    Result DeleteDefinition(string gambitId, string configName = "default");
}
