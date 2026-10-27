using Core.Combat.Models;
using Core.Run;
using Core.Common;
using Core.Math;
using System.Collections.Immutable;

namespace Core.Combat.Flow;

public enum CombatOutcomeEvaluationPoint
{
    LifecycleBoundary,
    ActionResolution
}

public interface ICombatOutcomeResolver
{
    Result<CombatState> EvaluateWithContext(RunState run, CombatState combat, OutcomePolicyDefinition policy,
        string? activeActorId, CombatOutcomeEvaluationPoint evaluationPoint, bool actionResolved = true);
    CombatState Evaluate(
        CombatState combat,
        OutcomePolicyDefinition policy,
        string? activeActorId,
        CombatOutcomeEvaluationPoint evaluationPoint,
        bool actionResolved = true);
}

/// <summary>
/// Pure outcome authority. Actor survival comes from configured resource
/// threshold policies; sides and controller bindings determine the winning
/// coalition without assigning meaning to a particular resource id.
/// </summary>
public sealed class CombatOutcomeResolver(IRuntimeFormulaEvaluator? formulas = null) : ICombatOutcomeResolver
{
    public Result<CombatState> EvaluateWithContext(RunState run, CombatState combat, OutcomePolicyDefinition policy,
        string? activeActorId, CombatOutcomeEvaluationPoint evaluationPoint, bool actionResolved = true)
    {
        if (policy.ParticipantVictory is not { } victory)
            return Result<CombatState>.Success(Evaluate(combat, policy, activeActorId, evaluationPoint, actionResolved));
        // Setup/lifecycle cannot win while hands are still being assembled. The authored
        // outcome boundary also controls resolution-stack timing.
        if (!combat.IsActive || evaluationPoint != CombatOutcomeEvaluationPoint.ActionResolution || !actionResolved ||
            !ShouldEvaluate(combat, policy, evaluationPoint, actionResolved)) return Result<CombatState>.Success(combat);
        if (formulas == null) return Result<CombatState>.Failure("Participant victory requires shared formula services");
        var winners = new List<CombatActorState>();
        foreach (var actor in combat.GetAllActors().Where(actor => actor.IsAlive).OrderBy(actor => actor.InstanceId, StringComparer.Ordinal))
        {
            var inputs = GameplayQueries.Evaluate(victory.Inputs, run, combat, actor.InstanceId);
            if (inputs.IsFailure) return Result<CombatState>.Failure(inputs.Error);
            var variables = Core.Calculations.GameplayFormulaContext.Build(actor, actor, actor, run, inputs.Value);
            var condition = Core.Calculations.GameplayFormulaContext.Evaluate(formulas, victory.Condition, run.Determinism.ContentRevision, variables);
            if (condition.IsFailure) return Result<CombatState>.Failure(condition.Error);
            if (condition.Value > 0) winners.Add(actor);
        }
        if (winners.Count == 0) return Result<CombatState>.Success(combat);
        if (string.IsNullOrWhiteSpace(victory.RuleId) || victory.TieBreak == ParticipantVictoryTieBreak.Unspecified || !Enum.IsDefined(victory.TieBreak))
            return Result<CombatState>.Failure("Participant victory requires ruleId and explicit tieBreak");
        var draw = winners.Count > 1 && victory.TieBreak == ParticipantVictoryTieBreak.Draw;
        if (!draw && victory.TieBreak != ParticipantVictoryTieBreak.All)
            winners = [victory.TieBreak == ParticipantVictoryTieBreak.ActiveActorThenOrdinal
                ? winners.FirstOrDefault(actor => actor.InstanceId == activeActorId) ?? winners[0] : winners[0]];
        var result = new ParticipantVictoryResult { RuleId = victory.RuleId, Draw = draw,
            ActorIds = draw ? [] : winners.Select(actor => actor.InstanceId).ToImmutableArray(),
            SideIds = draw ? [] : winners.Select(actor => actor.SideId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToImmutableArray() };
        return Result<CombatState>.Success(combat with { ParticipantVictory = result,
            Status = draw ? CombatStatus.DRAW : result.ActorIds.Contains(run.PlayerEntityId, StringComparer.Ordinal) ? CombatStatus.VICTORY : CombatStatus.DEFEAT });
    }

    public CombatState Evaluate(
        CombatState combat,
        OutcomePolicyDefinition policy,
        string? activeActorId,
        CombatOutcomeEvaluationPoint evaluationPoint,
        bool actionResolved = true)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.ParticipantVictory != null) return combat;
        if (!ShouldEvaluate(combat, policy, evaluationPoint, actionResolved))
            return combat;

        var participants = combat.GetAllActors().ToArray();
        var playerAnchors = participants
            .Where(actor => combat.ControllerOf(actor) == ControllerKind.Player)
            .ToArray();
        var playerActors = participants.Where(actor => playerAnchors.Any(anchor =>
            combat.Relationship(anchor, actor) == SideRelationship.Ally)).ToArray();
        var aiActors = participants.Where(actor => playerAnchors.Any(anchor =>
            combat.Relationship(anchor, actor) == SideRelationship.Enemy)).ToArray();
        var playersDefeated = playerActors.Length == 0 || playerActors.All(actor => !actor.IsAlive);
        var aiDefeated = aiActors.Length == 0 || aiActors.All(actor => !actor.IsAlive);
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

    private static bool ShouldEvaluate(
        CombatState combat,
        OutcomePolicyDefinition policy,
        CombatOutcomeEvaluationPoint evaluationPoint,
        bool actionResolved)
    {
        if (evaluationPoint == CombatOutcomeEvaluationPoint.LifecycleBoundary)
            return combat.PriorityWindow == null && combat.PendingActions.IsEmpty;

        return policy.EvaluationBoundary switch
        {
            OutcomeEvaluationBoundary.Immediate => true,
            OutcomeEvaluationBoundary.AfterCurrentAction => actionResolved,
            OutcomeEvaluationBoundary.AfterResolutionStack =>
                actionResolved && combat.PriorityWindow == null && combat.PendingActions.IsEmpty,
            _ => false
        };
    }
}
