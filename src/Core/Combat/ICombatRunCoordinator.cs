using Core.Combat.Models;
using Core.Common;

namespace Core.Combat;

public interface ICombatRunCoordinator
{
    Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        string heroId,
        IReadOnlyList<string> enemyIds,
        int initialEnergy = 3);
    Result<CombatRunEncounterResult> GetCurrentEncounter(Guid runId);
    Result<CombatRunEncounterResult> GetCombatState(Guid combatId);
    Result<CombatRunActionResult> ExecuteAction(Guid combatId, CombatActionCommand command);
    Result<CombatRunEncounterResult> ResolveEncounter(Guid runId, Guid combatId);
}
