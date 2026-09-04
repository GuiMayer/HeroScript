using Core.Combat.Models;
using Core.Common;

namespace Core.Combat;

internal static class CombatSystemTestExtensions
{
    public static Result<CombatState> StartCombat(
        this ICombatSystem combatSystem,
        string heroDefinitionId,
        IReadOnlyList<string> enemyDefinitionIds) =>
        combatSystem.StartCombat(
            new CombatParticipantReference(heroDefinitionId, heroDefinitionId),
            enemyDefinitionIds
                .Select(id => new CombatParticipantReference(id, id))
                .ToArray());

    public static Result<CombatState> StartCombat(
        this ICombatSystem combatSystem,
        string heroDefinitionId,
        IReadOnlyList<string> enemyDefinitionIds,
        CombatStartOptions options) =>
        combatSystem.StartCombat(
            new CombatParticipantReference(heroDefinitionId, heroDefinitionId),
            enemyDefinitionIds
                .Select(id => new CombatParticipantReference(id, id))
                .ToArray(),
            options);
}
