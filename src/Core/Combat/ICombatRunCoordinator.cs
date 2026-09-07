using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Core.StatusEffects;
using System.Text.Json;

namespace Core.Combat;

public interface ICombatRunCoordinator
{
    Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        CombatParticipantReference hero,
        IReadOnlyList<CombatParticipantReference> enemies,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? initialResourceValues = null,
        RunCommandIdentity? commandIdentity = null,
        JsonElement commandPayload = default);
    Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        CombatEntity hero,
        IReadOnlyList<CombatEntity> enemies,
        RunCommandIdentity? commandIdentity = null,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatusEffects = null,
        JsonElement commandPayload = default);
    Result<CombatRunEncounterResult> GetCurrentEncounter(Guid runId);
    Result<CombatRunEncounterResult> GetCombatState(Guid combatId);
    Result<CombatRunActionResult> ExecuteAction(
        Guid combatId,
        CombatActionCommand command,
        RunCommandIdentity? commandIdentity = null,
        JsonElement commandPayload = default);
    Result<CombatRunEncounterResult> ResolveEncounter(
        Guid runId,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null,
        JsonElement commandPayload = default);
}
