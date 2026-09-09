using Core.Combat.Models;

namespace Core.Combat.Flow;

public enum CombatOutcomeEvaluationPoint
{
    LifecycleBoundary,
    ActionResolution
}

public interface ICombatOutcomeResolver
{
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
public sealed class CombatOutcomeResolver : ICombatOutcomeResolver
{
    public CombatState Evaluate(
        CombatState combat,
        OutcomePolicyDefinition policy,
        string? activeActorId,
        CombatOutcomeEvaluationPoint evaluationPoint,
        bool actionResolved = true)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(policy);
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
