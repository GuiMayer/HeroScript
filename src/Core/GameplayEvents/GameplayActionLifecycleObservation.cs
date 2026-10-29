using Core.Combat.LegalActions;
using Core.Combat.Reactions;
using Core.Common;

namespace Core.GameplayEvents;

/// <summary>Facts about transitions already authorized by the existing priority/action reducers.</summary>
internal static class GameplayActionLifecycleObservation
{
    internal static Result<GameplayEffectObservation> Append(GameplayEffectObservation observation, LegalActionCandidate candidate)
    {
        var kind = candidate.ReactionTransition switch
        {
            ReactionTransitionKind.Proposed => "action.proposed",
            ReactionTransitionKind.StackActionResolved => "action.resolved",
            ReactionTransitionKind.StackActionFizzled => "action.cancelled",
            _ => candidate.ResolvedCommand != null ? "action.resolved" : null
        };
        if (kind == null) return Result<GameplayEffectObservation>.Success(observation);
        var command = candidate.PendingAction?.Command ?? candidate.ResolvedCommand ?? candidate.Command;
        var causal = observation.WithActionCause(kind == "action.proposed" ? null : candidate.PendingAction?.OriginCause);
        var appended = observation.Events.Append(observation.PublisherId, $"action/{observation.Events.Occurrences.Count}/transition", new()
        {
            Kind = kind, ReasonId = "command.input", Attribution = GameplayAttribution.Source,
            SourceActorId = command.ActorId, SubjectActorId = command.ActorId,
            OriginCause = causal.HistoricalActionCause,
            Origin = new() { ContainerKind = "rule", ContainerId = "combat.action", ComponentId = command.PowerId ?? command.ActionType.ToString(),
                ContentRevision = observation.Events.Scope.ContentRevision },
            Payload = new ActionOccurrencePayload { ActionDefinitionId = command.PowerId ?? command.ActionType.ToString(),
                PendingActionId = candidate.PendingAction?.PendingActionId,
                PriorityWindowId = candidate.SuccessorCombat.PriorityWindow?.WindowId }
        }, causal.ParentEventId);
        return appended.IsFailure ? Result<GameplayEffectObservation>.Failure(appended.Error)
            : Result<GameplayEffectObservation>.Success(observation with { Events = appended.Value.Transaction });
    }
}
