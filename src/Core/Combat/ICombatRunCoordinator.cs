using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Core.StatusEffects;

namespace Core.Combat;

public interface ICombatRunCoordinator
{
    Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        CombatParticipantReference hero,
        IReadOnlyList<CombatParticipantReference> enemies,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? initialResourceValues = null,
        RunCommandIdentity? commandIdentity = null);
    Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        CombatEntity hero,
        IReadOnlyList<CombatEntity> enemies,
        RunCommandIdentity? commandIdentity = null,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatusEffects = null);
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
