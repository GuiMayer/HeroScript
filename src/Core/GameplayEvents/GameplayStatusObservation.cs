using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.StatusEffects;

namespace Core.GameplayEvents;

public sealed record ObservedStatusLifecycleExecution(CombatStatusLifecycleResult Execution, GameplayEffectObservation Observation);

public static class GameplayStatusObservation
{
    /// <summary>The boundary advances duration; it is not an attack by the actor that originally applied the status.</summary>
    internal static Result<GameplayEffectObservation> Tick(GameplayEffectObservation observation, StatusEffectInstance before,
        StatusEffectInstance? after)
    {
        var metadata = new GameplayOccurrenceDraft
        {
            ReasonId = observation.ReasonId, Attribution = GameplayAttribution.None, SubjectActorId = before.TargetId,
            OriginCause = before.OriginCause,
            Origin = new() { ContainerKind = "rule", ContainerId = "status.lifecycle",
                ComponentId = before.InstanceId.ToString("D"), ContentRevision = observation.Events.Scope.ContentRevision }
        };
        var draft = after == null ? GameplayEntityFactProducer.Status(new()
        {
            Store = EffectStackStore.Status, InstanceId = before.InstanceId, DefinitionId = before.StatusId,
            Owner = new() { Kind = GameplayOwnerKind.Entity, Id = before.TargetId },
            PreviousStacks = before.Stacks, CurrentStacks = 0, Reason = EffectStackChangeReason.Expire
        }, before, null, metadata) : GameplayEntityFactProducer.StatusDuration(before, after, metadata);
        if (draft.IsFailure) return Result<GameplayEffectObservation>.Failure(draft.Error);
        var appended = observation.Events.Append(observation.PublisherId, $"status.lifecycle/{before.InstanceId:D}/{before.Duration}",
            draft.Value, observation.ParentEventId);
        return appended.IsFailure ? Result<GameplayEffectObservation>.Failure(appended.Error)
            : Result<GameplayEffectObservation>.Success(observation with { Events = appended.Value.Transaction });
    }
}
