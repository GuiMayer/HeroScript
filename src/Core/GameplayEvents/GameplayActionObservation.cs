using System.Collections.Immutable;
using Core.Common;

namespace Core.GameplayEvents;

public static class GameplayActionObservation
{
    public static Result<GameplayEffectObservation> Begin(GameplayEffectObservation observation, string actionId, string actorId)
    {
        if (observation?.Events == null) return Result<GameplayEffectObservation>.Failure("Action observation requires a transaction");
        var appended = observation.Events.Append(observation.PublisherId, $"command/{observation.Events.Occurrences.Count}/start", new()
        {
            Kind = "action.started", ReasonId = observation.ReasonId, Attribution = GameplayAttribution.Source,
            SourceActorId = actorId, SubjectActorId = actorId,
            Origin = new() { ContainerKind = "rule", ContainerId = "combat.command", ComponentId = actionId, ContentRevision = observation.Events.Scope.ContentRevision },
            Payload = new ActionOccurrencePayload { ActionDefinitionId = actionId }
        }, observation.ParentEventId);
        return appended.IsFailure ? Result<GameplayEffectObservation>.Failure(appended.Error)
            : Result<GameplayEffectObservation>.Success(observation with { Events = appended.Value.Transaction, ParentEventId = appended.Value.Occurrence.EventId });
    }

    public static Result<GameplayEffectObservation> Complete(GameplayEffectObservation before, GameplayEffectObservation started,
        GameplayEffectObservation finished, string actionId, string actorId)
    {
        if (before?.Events == null || started?.Events == null || finished?.Events == null ||
            started.Events.Scope != finished.Events.Scope || started.ParentEventId == null ||
            !started.Events.Occurrences.Select(item => item.EventId).SequenceEqual(finished.Events.Occurrences.Take(started.Events.Occurrences.Count).Select(item => item.EventId)))
            return Result<GameplayEffectObservation>.Failure("Action candidate does not descend from its observation input");
        var appended = finished.Events.Append(finished.PublisherId, $"command/{finished.Events.Occurrences.Count}/complete", new()
        {
            Kind = "action.completed", ReasonId = finished.ReasonId, Attribution = GameplayAttribution.Source,
            SourceActorId = actorId, SubjectActorId = actorId,
            Origin = new() { ContainerKind = "rule", ContainerId = "combat.command", ComponentId = actionId, ContentRevision = finished.Events.Scope.ContentRevision },
            Payload = new ActionOccurrencePayload { ActionDefinitionId = actionId,
                ChildEventIds = finished.Events.Occurrences.Skip(started.Events.Occurrences.Count).Select(item => item.EventId).ToImmutableArray() }
        }, started.ParentEventId);
        return appended.IsFailure ? Result<GameplayEffectObservation>.Failure(appended.Error)
            : Result<GameplayEffectObservation>.Success(finished with { Events = appended.Value.Transaction, ParentEventId = before.ParentEventId });
    }
}
