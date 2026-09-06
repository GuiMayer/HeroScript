using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.Combat.Flow;

/// <summary>Pure deterministic transitions shared by every combat adapter.</summary>
public static class CombatFlowTransitions
{
    /// <summary>
    /// Materializes commands that have no effect graph. PASS and END_TURN are
    /// timeline facts only; activation lifecycle is planned separately.
    /// </summary>
    public static Result<CombatState> AppendPassiveCommand(
        CombatState combat,
        CombatActionCommand command)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(command);
        if (!combat.IsActive)
            return Result<CombatState>.Failure($"Combat is not active: {combat.CombatId}");
        if (command.ActionType is not ActionType.PASS and not ActionType.END_TURN)
        {
            return Result<CombatState>.Failure(
                $"Action requires an effect executor: {command.ActionType}");
        }
        if (command.ExpectedStep.HasValue && command.ExpectedStep.Value != combat.Determinism.Step)
        {
            return Result<CombatState>.Failure(
                $"Stale combat command: expected step {command.ExpectedStep.Value}, current step is {combat.Determinism.Step}");
        }

        var actor = combat.GetEntity(command.ActorId);
        if (actor == null)
            return Result<CombatState>.Failure($"Actor not found: {command.ActorId}");
        if (!actor.IsAlive)
            return Result<CombatState>.Failure($"Actor is not alive: {command.ActorId}");

        var appended = CombatTransitions.AppendAction(combat, new CombatAction
        {
            Turn = combat.CurrentTurn,
            ActorId = command.ActorId,
            ActionType = command.ActionType
        });
        return Result<CombatState>.Success(appended.State);
    }

    public static Result ValidateCommandInput(
        CombatState combat,
        CombatActionCommand command)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(command);
        var activation = combat.ActivationState;
        if (activation == null)
            return Result.Failure("Combat activation has not been initialized");
        if (!activation.WaitingForInput)
            return Result.Failure("Combat is resolving automatic actions");
        if (!string.Equals(activation.ActiveActorId, command.ActorId, StringComparison.Ordinal))
            return Result.Failure($"Actor '{command.ActorId}' is not the active actor");
        var phase = combat.PhaseState?.PhaseSequence.Find(combat.PhaseState.CurrentPhaseId);
        if (phase == null)
            return Result.Failure("Combat phase has not been initialized");
        return phase.AllowedActions.Contains(command.ActionType)
            ? Result.Success()
            : Result.Failure(
                $"Action '{command.ActionType}' is not allowed in phase '{phase.PhaseId}'");
    }

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

        var actor = combat.GetEntity(command.ActorId);
        if (actor == null)
            return Result.Failure($"Actor not found: {command.ActorId}");
        if (!ScopeApplies(policy.ActorScope, actor))
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
            combat.ActivationState == null ||
            combat.GetEntity(command.ActorId) is not { } actor ||
            !ScopeApplies(policy.ActorScope, actor))
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

    public static CombatState EvaluateOutcome(
        CombatState combat,
        OutcomePolicyDefinition policy,
        string? activeActorId)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(policy);
        var heroesDefeated = combat.HeroIsDead;
        var enemiesDefeated = combat.AllEnemiesDead;
        if (!heroesDefeated && !enemiesDefeated)
            return combat with { Status = CombatStatus.ACTIVE };
        if (!heroesDefeated)
            return combat with { Status = CombatStatus.VICTORY };
        if (!enemiesDefeated)
            return combat with { Status = CombatStatus.DEFEAT };

        var status = policy.TieBreak switch
        {
            OutcomeTieBreak.Draw => CombatStatus.DRAW,
            OutcomeTieBreak.HeroesWin => CombatStatus.VICTORY,
            OutcomeTieBreak.EnemiesWin => CombatStatus.DEFEAT,
            OutcomeTieBreak.ActiveActorWins =>
                string.Equals(activeActorId, combat.Hero.EntityId, StringComparison.Ordinal)
                    ? CombatStatus.VICTORY
                    : CombatStatus.DEFEAT,
            _ => CombatStatus.DRAW
        };
        return combat with { Status = status };
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

    private static bool ScopeApplies(FlowActorScope scope, CombatEntity actor) => scope switch
    {
        FlowActorScope.Player => actor.IsHero,
        FlowActorScope.Enemies => !actor.IsHero,
        FlowActorScope.All => true,
        _ => false
    };

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
