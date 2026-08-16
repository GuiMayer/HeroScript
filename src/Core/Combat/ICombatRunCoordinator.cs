using Core.Combat.Models;
using Core.Common;
using Core.Run;

namespace Core.Combat;

public interface ICombatRunCoordinator
{
    Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        string heroId,
        IReadOnlyList<string> enemyIds,
        int initialEnergy = 3,
        RunCommandIdentity? commandIdentity = null);
    Result<CombatRunEncounterResult> GetCurrentEncounter(Guid runId);
    Result<CombatRunEncounterResult> GetCombatState(Guid combatId);
    Result<CombatRunActionResult> ExecuteAction(
        Guid combatId,
        CombatActionCommand command,
        RunCommandIdentity? commandIdentity = null);
    Result<CombatRunEncounterResult> ResolveEncounter(
        Guid runId,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null);
}
