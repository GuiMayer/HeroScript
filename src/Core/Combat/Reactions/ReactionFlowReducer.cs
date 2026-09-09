using System.Collections.Immutable;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;

namespace Core.Combat.Reactions;

public sealed record PriorityPassResult(
    CombatState Combat,
    PendingActionState? ActionToResolve);

public interface IReactionFlowReducer
{
    bool ShouldPropose(CombatState combat, ReactionPolicyDefinition policy, IReadOnlySet<string> commandTags);
    Result<CombatState> Propose(CombatState combat, PendingActionState pending, ReactionPolicyDefinition policy);
    Result<PriorityPassResult> Pass(CombatState combat, string actorId, ReactionPolicyDefinition policy);
    Result<CombatState> CompleteResolution(
        CombatState combat,
        PendingActionState resolved,
        ReactionPolicyDefinition policy);
}

/// <summary>
/// Pure priority/stack state machine. It never executes gameplay effects and
/// therefore cannot become a second authority for action legality or resolution.
/// </summary>
public sealed class ReactionFlowReducer : IReactionFlowReducer
{
    public bool ShouldPropose(
        CombatState combat,
        ReactionPolicyDefinition policy,
        IReadOnlySet<string> commandTags)
    {
        if (policy.Strategy != ReactionStrategy.PriorityStack)
            return false;
        if (combat.PhaseState == null)
            return false;
        if (combat.PriorityWindow != null)
            return policy.ResponseActionTags.Count == 0 ||
                   policy.ResponseActionTags.Any(tag => commandTags.Contains(tag));
        return policy.OpeningActionTags.Count == 0 ||
               policy.OpeningActionTags.Any(tag => commandTags.Contains(tag));
    }

    public Result<CombatState> Propose(
        CombatState combat,
        PendingActionState pending,
        ReactionPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Strategy != ReactionStrategy.PriorityStack)
            return Result<CombatState>.Failure("Only PriorityStack can create pending actions");
        if (string.IsNullOrWhiteSpace(pending.PendingActionId))
            return Result<CombatState>.Failure("Pending action id is required");
        if (pending.Depth != combat.PendingActions.Length + 1)
            return Result<CombatState>.Failure(
                $"Pending action depth must be {combat.PendingActions.Length + 1}");
        if (combat.PriorityWindow != null && !string.Equals(
                combat.PriorityWindow.HolderActorId,
                pending.Command.ActorId,
                StringComparison.Ordinal))
        {
            return Result<CombatState>.Failure(
                $"Actor '{pending.Command.ActorId}' does not hold priority");
        }
        if (combat.PendingActions.Length >= policy.MaxStackDepth)
            return Result<CombatState>.Failure(
                $"Reaction stack depth limit reached: {policy.MaxStackDepth}");
        if (combat.PendingActions.Any(item =>
                string.Equals(item.PendingActionId, pending.PendingActionId, StringComparison.Ordinal)))
            return Result<CombatState>.Failure($"Pending action already exists: {pending.PendingActionId}");

        var eligible = ResolveEligibleActors(combat, pending.Command.ActorId, policy);
        if (eligible.Length == 0)
            return Result<CombatState>.Failure("Priority window has no eligible actors");
        var holderIndex = NextIndex(eligible, pending.Command.ActorId);
        var pendingActions = combat.PendingActions.Append(pending).ToImmutableArray();
        var windowId = CanonicalJson.ComputeHash(new
        {
            combat.CombatId,
            pending.PendingActionId,
            depth = pendingActions.Length,
            combat.Determinism.Step
        });
        return Result<CombatState>.Success(combat with
        {
            Determinism = combat.Determinism.AdvanceStep(),
            PendingActions = pendingActions,
            PriorityWindow = new PriorityWindowState
            {
                WindowId = windowId,
                OpenedByActorId = pending.Command.ActorId,
                EligibleActorIds = eligible,
                HolderIndex = holderIndex,
                HolderActorId = eligible[holderIndex],
                ConsecutivePasses = 0,
                ResolutionCount = combat.PriorityWindow?.ResolutionCount ?? 0
            }
        });
    }

    public Result<PriorityPassResult> Pass(
        CombatState combat,
        string actorId,
        ReactionPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(combat);
        var window = combat.PriorityWindow;
        if (window == null || combat.PendingActions.Length == 0)
            return Result<PriorityPassResult>.Failure("No priority window is open");
        if (!string.Equals(window.HolderActorId, actorId, StringComparison.Ordinal))
            return Result<PriorityPassResult>.Failure($"Actor '{actorId}' does not hold priority");

        var appended = CombatTransitions.AppendAction(combat, new CombatAction
        {
            Turn = combat.CurrentTurn,
            ActorId = actorId,
            ActionType = ActionType.PASS_PRIORITY
        }).State;
        var passes = window.ConsecutivePasses + 1;
        if (passes < window.EligibleActorIds.Count)
        {
            var nextIndex = (window.HolderIndex + 1) % window.EligibleActorIds.Count;
            return Result<PriorityPassResult>.Success(new(
                appended with
                {
                    PriorityWindow = window with
                    {
                        HolderIndex = nextIndex,
                        HolderActorId = window.EligibleActorIds[nextIndex],
                        ConsecutivePasses = passes
                    }
                },
                null));
        }

        var selectedIndex = policy.StackOrder == ReactionStackOrder.Fifo
            ? 0
            : combat.PendingActions.Length - 1;
        var selected = combat.PendingActions[selectedIndex];
        var remaining = combat.PendingActions.Where((_, index) => index != selectedIndex).ToImmutableArray();
        return Result<PriorityPassResult>.Success(new(
            appended with
            {
                PendingActions = remaining,
                PriorityWindow = window with { ConsecutivePasses = passes }
            },
            selected));
    }

    public Result<CombatState> CompleteResolution(
        CombatState combat,
        PendingActionState resolved,
        ReactionPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(resolved);
        if (combat.PendingActions.Length == 0)
            return Result<CombatState>.Success(combat with { PriorityWindow = null });

        var next = policy.StackOrder == ReactionStackOrder.Fifo
            ? combat.PendingActions[0]
            : combat.PendingActions[^1];
        var eligible = ResolveEligibleActors(combat, next.Command.ActorId, policy);
        if (eligible.Length == 0)
            return Result<CombatState>.Failure("Priority window has no eligible actors after resolution");
        var holderIndex = NextIndex(eligible, resolved.Command.ActorId);
        return Result<CombatState>.Success(combat with
        {
            PriorityWindow = new PriorityWindowState
            {
                WindowId = CanonicalJson.ComputeHash(new
                {
                    combat.CombatId,
                    nextPendingActionId = next.PendingActionId,
                    resolvedPendingActionId = resolved.PendingActionId,
                    resolution = (combat.PriorityWindow?.ResolutionCount ?? 0) + 1,
                    combat.Determinism.Step
                }),
                OpenedByActorId = next.Command.ActorId,
                EligibleActorIds = eligible,
                HolderIndex = holderIndex,
                HolderActorId = eligible[holderIndex],
                ConsecutivePasses = 0,
                ResolutionCount = (combat.PriorityWindow?.ResolutionCount ?? 0) + 1
            }
        });
    }

    private static ImmutableArray<string> ResolveEligibleActors(
        CombatState combat,
        string proposerId,
        ReactionPolicyDefinition policy)
    {
        var proposer = combat.GetActor(proposerId);
        var preferred = combat.TurnOrderState.Order.Count > 0
            ? combat.TurnOrderState.Order
            : combat.ActorOrder.Count > 0
                ? combat.ActorOrder
                : combat.GetAllActors().Select(actor => actor.InstanceId).ToArray();
        return preferred
            .Distinct(StringComparer.Ordinal)
            .Select(combat.GetActor)
            .Where(actor => actor is { IsAlive: true })
            .Where(actor => policy.Eligibility != ReactionActorEligibility.OpponentsOnly ||
                            proposer == null || combat.Relationship(proposer, actor!) == SideRelationship.Enemy)
            .Select(actor => actor!.InstanceId)
            .ToImmutableArray();
    }

    private static int NextIndex(IReadOnlyList<string> eligible, string afterActorId)
    {
        var current = -1;
        for (var index = 0; index < eligible.Count; index++)
        {
            if (!string.Equals(eligible[index], afterActorId, StringComparison.Ordinal)) continue;
            current = index;
            break;
        }
        return current < 0 ? 0 : (current + 1) % eligible.Count;
    }
}
