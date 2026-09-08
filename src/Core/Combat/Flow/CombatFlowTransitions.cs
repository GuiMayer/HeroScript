using Core.Combat.Models;
using Core.Common;
using Core.Run;

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

        var actor = combat.GetActor(command.ActorId);
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
        if (combat.PhaseState == null)
            return Result.Failure("Combat phase has not been initialized");
        return Result.Success();
    }

    public static Result ValidateActionBudget(
        RunState run,
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

        var actor = combat.GetActor(command.ActorId);
        if (actor == null)
            return Result.Failure($"Actor not found: {command.ActorId}");
        if (!ScopeApplies(policy.ActorScope, combat, run, actor))
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
        RunState run,
        CombatState combat,
        CombatActionCommand command,
        ActionBudgetPolicyDefinition policy,
        string commandType)
    {
        if (policy.Strategy != ActionBudgetStrategy.FixedCount ||
            command.ActionType is ActionType.END_TURN or ActionType.PASS ||
            !policy.ConsumingCommands.Contains(commandType, StringComparer.Ordinal) ||
            combat.ActivationState == null ||
            combat.GetActor(command.ActorId) is not { } actor ||
            !ScopeApplies(policy.ActorScope, combat, run, actor))
            return combat;

        return combat with
        {
            ActivationState = combat.ActivationState with
            {
                ActionsTaken = checked(combat.ActivationState.ActionsTaken + 1)
            }
        };
    }

    public static CombatState EvaluateOutcome(
        CombatState combat,
        OutcomePolicyDefinition policy,
        string? activeActorId)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(policy);
        var participants = combat.GetAllActors().ToArray();
        var playerControlled = participants
            .Where(entity => combat.ControllerOf(entity) == ControllerKind.Player)
            .ToArray();
        var aiControlled = participants
            .Where(entity => combat.ControllerOf(entity) == ControllerKind.AI)
            .ToArray();
        var playersDefeated = playerControlled.Length == 0 || playerControlled.All(entity => !entity.IsAlive);
        var aiDefeated = aiControlled.Length == 0 || aiControlled.All(entity => !entity.IsAlive);
        if (!playersDefeated && !aiDefeated)
            return combat with { Status = CombatStatus.ACTIVE };
        if (!playersDefeated)
            return combat with { Status = CombatStatus.VICTORY };
        if (!aiDefeated)
            return combat with { Status = CombatStatus.DEFEAT };

        var status = policy.TieBreak switch
        {
            OutcomeTieBreak.Draw => CombatStatus.DRAW,
            OutcomeTieBreak.PlayerControlledWins => CombatStatus.VICTORY,
            OutcomeTieBreak.AiControlledWins => CombatStatus.DEFEAT,
            OutcomeTieBreak.ActiveActorWins =>
                combat.GetActor(activeActorId ?? string.Empty) is { } activeActor &&
                combat.ControllerOf(activeActor) == ControllerKind.Player
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
        var actor = combat.GetActor(command.ActorId);
        if (actor == null)
            return Result.Failure($"Actor not found: {command.ActorId}");
        return actor.GetResource(policy.ResourceId!) == null
            ? Result.Failure($"Action budget resource not found: {policy.ResourceId}")
            : Result.Success();
    }

    private static bool ScopeApplies(
        FlowActorScope scope,
        CombatState combat,
        RunState run,
        CombatActorState actor) => scope switch
    {
        FlowActorScope.RunOwner => string.Equals(actor.InstanceId, run.PlayerEntityId, StringComparison.Ordinal),
        FlowActorScope.PlayerControlled => combat.ControllerOf(actor) == ControllerKind.Player,
        FlowActorScope.AiControlled => combat.ControllerOf(actor) == ControllerKind.AI,
        FlowActorScope.All => true,
        _ => false
    };

}
