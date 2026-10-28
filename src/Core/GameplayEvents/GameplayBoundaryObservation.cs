using Core.Common;

namespace Core.GameplayEvents;

/// <summary>Records an already chosen boundary. It never chooses, advances or executes a flow.</summary>
public static class GameplayBoundaryObservation
{
    public static Result<GameplayEffectObservation> Begin(GameplayEffectObservation observation, GameplayBoundary boundary,
        string boundaryId, string? actorId, GameplayEventOrigin origin, string producerPath)
    {
        if (observation?.Events == null) return Result<GameplayEffectObservation>.Failure("Boundary observation requires a transaction");
        var fact = GameplayEntityFactProducer.Boundary(boundary, boundaryId, observation.Events.Occurrences.Count, new()
        {
            ReasonId = observation.ReasonId, Attribution = GameplayAttribution.None, SubjectActorId = actorId, Origin = origin
        });
        if (fact.IsFailure) return Result<GameplayEffectObservation>.Failure(fact.Error);
        var appended = observation.Events.Append(observation.PublisherId, producerPath, fact.Value, observation.ParentEventId);
        return appended.IsFailure ? Result<GameplayEffectObservation>.Failure(appended.Error)
            : Result<GameplayEffectObservation>.Success(observation with { Events = appended.Value.Transaction,
                ParentEventId = appended.Value.Occurrence.EventId, LastApplicationEventId = appended.Value.Occurrence.EventId });
    }
}
