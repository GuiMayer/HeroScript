using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;
using Core.Resources;

namespace Core.Combat.Flow;

public sealed partial class CombatBoundaryExecutor
{
    private readonly IGameplayOccurrenceExecutor? _occurrences;

    private Result<T> WithOccurrenceObservation<T>(RunState run, CombatState combat, Func<ObservationCursor?, Result<T>> execute)
    {
        if (_occurrences == null) return execute(null);
        var prepared = _occurrences.PrepareOccurrenceObservation(run, combat);
        if (prepared.IsFailure) return Result<T>.Failure(prepared.Error);
        if (prepared.Value == null) return execute(null);
        var result = Observe(run, prepared.Value, execute);
        return result.IsFailure ? Result<T>.Failure(result.Error) : Result<T>.Success(result.Value.Execution);
    }

    private static EffectBatchResult BoundaryResponses(ObservationCursor? observation, RunState run, CombatState combat) =>
        observation?.Responses(run, combat) ?? new() { Run = run, State = combat };
    private static Result<EffectBatchResult> RefreshActorResource(
        CombatState combat,
        string actorId,
        ResourceCyclePolicyDefinition policy,
        RunState run, ObservationCursor? observation = null)
    {
        if (!ScopeApplies(policy.ActorScope, combat, run, actorId) ||
            policy.StartActivation == ResourceRefreshStrategy.Preserve)
            return Result<EffectBatchResult>.Success(new() { State = combat, Run = run });

        var actor = combat.GetActor(actorId);
        if (actor == null)
            return Result<EffectBatchResult>.Failure($"Actor not found: {actorId}");
        var resource = actor.GetResource(policy.ResourceId);
        if (resource == null)
            return Result<EffectBatchResult>.Failure(
                $"Activation resource '{policy.ResourceId}' was not found on actor '{actorId}'");
        var value = policy.StartActivation switch
        {
            ResourceRefreshStrategy.ResetToMax => resource.Maximum,
            ResourceRefreshStrategy.Add => resource.Current + policy.Amount!.Value,
            ResourceRefreshStrategy.Set => policy.Amount!.Value,
            _ => resource.Current
        };
        var mutation = new ResolvedResourceMutation
        {
            MutationId = $"resource-cycle:start-activation:{actorId}:{policy.ResourceId}",
            ResourceId = policy.ResourceId,
            Operation = ResourceMutationOperation.Set,
            Value = value
        };
        var reduced = new ResourceMutationReducer().Apply(actor.ResourceState.Resources, [mutation]);
        if (reduced.IsFailure)
            return Result<EffectBatchResult>.Failure(reduced.Error);
        var resources = actor.ResourceState with { Resources = reduced.Value.Resources };
        var updated = actor.WithResourceState(resources);
        var beforeObservation = observation?.Current;
        observation?.ResourceMutation(mutation, reduced.Value.Records.Single(), actor, updated);
        var after = combat.ReplaceActor(updated);
        return Result<EffectBatchResult>.Success(beforeObservation == null ? new() { State = after, Run = run }
            : observation!.Mutations(run, combat, run, after, beforeObservation));
    }

}
