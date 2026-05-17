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
}
