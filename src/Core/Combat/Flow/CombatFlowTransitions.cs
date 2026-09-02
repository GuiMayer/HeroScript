using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.Combat.Flow;

/// <summary>Pure deterministic transitions shared by every combat adapter.</summary>
public static class CombatFlowTransitions
{
    public static Result ValidateActionBudget(
        CombatState combat,
        CombatActionCommand command,
        ActionBudgetPolicyDefinition policy,
        string commandType)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(policy);

        if (command.ActionType is ActionType.END_TURN or ActionType.PASS ||
            !policy.ConsumingCommands.Contains(commandType, StringComparer.Ordinal))
            return Result.Success();

        return policy.Strategy switch
        {
            ActionBudgetStrategy.ResourceLimited => ValidateResourceBudget(combat, command, policy),
            ActionBudgetStrategy.FixedCount =>
                (combat.ActivationState?.ActionsTaken ?? 0) < policy.MaxActionsPerActivation
                    ? Result.Success()
                    : Result.Failure(
                        $"Action budget exhausted: {policy.MaxActionsPerActivation} actions per activation"),
            _ => Result.Failure($"Unsupported action budget strategy: {policy.Strategy}")
        };
    }

    public static CombatState ConsumeActionBudget(
        CombatState combat,
        CombatActionCommand command,
        ActionBudgetPolicyDefinition policy,
        string commandType)
    {
        if (policy.Strategy != ActionBudgetStrategy.FixedCount ||
            command.ActionType is ActionType.END_TURN or ActionType.PASS ||
            !policy.ConsumingCommands.Contains(commandType, StringComparer.Ordinal) ||
            combat.ActivationState == null)
            return combat;

        return combat with
        {
            ActivationState = combat.ActivationState with
            {
                ActionsTaken = checked(combat.ActivationState.ActionsTaken + 1)
            }
        };
    }

    public static IReadOnlyList<string> CreateRoundSnapshotOrder(
        CombatState combat,
        ActivationOrderPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(policy);
        var alive = combat.GetAllEntities().Where(entity => entity.IsAlive).ToArray();
        var indexedOrder = (combat.TurnOrder ?? [])
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);

        return alive
            .OrderBy(entity => indexedOrder.TryGetValue(entity.EntityId, out var index) ? index : int.MaxValue)
            .ThenBy(entity => TieRank(entity, policy.TieBreak))
            .ThenBy(entity => TieKey(combat, entity, policy.TieBreak), StringComparer.Ordinal)
            .Select(entity => entity.EntityId)
            .ToArray();
    }

    private static Result ValidateResourceBudget(
        CombatState combat,
        CombatActionCommand command,
        ActionBudgetPolicyDefinition policy)
    {
        var actor = combat.GetEntity(command.ActorId);
        if (actor == null)
            return Result.Failure($"Actor not found: {command.ActorId}");
        return actor.GetResource(policy.ResourceId!) == null
            ? Result.Failure($"Action budget resource not found: {policy.ResourceId}")
            : Result.Success();
    }

    private static int TieRank(CombatEntity entity, ActivationTieBreak tieBreak) => tieBreak switch
    {
        ActivationTieBreak.HeroesFirst => entity.IsHero ? 0 : 1,
        ActivationTieBreak.EnemiesFirst => entity.IsHero ? 1 : 0,
        _ => 0
    };

    private static string TieKey(
        CombatState combat,
        CombatEntity entity,
        ActivationTieBreak tieBreak) => tieBreak == ActivationTieBreak.SeededRandom
        ? CanonicalJson.ComputeHash(new { combat.Determinism.Seed, entity.EntityId })
        : entity.EntityId;
}
