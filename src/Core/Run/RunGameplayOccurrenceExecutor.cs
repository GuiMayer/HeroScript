using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.GameplayEvents;

namespace Core.Run;

public interface IRunGameplayOccurrenceExecutor
{
    Result<GameplayEffectObservation?> Prepare(RunState run, GameplayEffectObservation? observation = null);
    Result<ObservedGameplayExecution<RunState>> Dispatch(RunState before, RunState after,
        GameplayEffectObservation beforeObservation, GameplayEffectObservation afterObservation);
}

/// <summary>Adapts persistent actors to the existing dispatcher. A run wallet never becomes an actor's resource component.</summary>
public sealed class RunGameplayOccurrenceExecutor(IGameplayOccurrenceExecutor executor) : IRunGameplayOccurrenceExecutor
{
    public Result<GameplayEffectObservation?> Prepare(RunState run, GameplayEffectObservation? observation = null) =>
        executor.PrepareOccurrenceObservation(run, Snapshot(run), observation);

    public Result<ObservedGameplayExecution<RunState>> Dispatch(RunState before, RunState after,
        GameplayEffectObservation beforeObservation, GameplayEffectObservation afterObservation)
    {
        var prepared = Prepare(after);
        if (prepared.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(prepared.Error);
        if (prepared.Value == null) return Result<ObservedGameplayExecution<RunState>>.Success(new(after, afterObservation));
        if (before.ActiveEncounterId != null || after.ActiveEncounterId != null)
            return Result<ObservedGameplayExecution<RunState>>.Failure("Persistent occurrences cannot replace an active encounter");
        var result = executor.DispatchOccurrencesObserved(before, Snapshot(before), after, Snapshot(after), beforeObservation, afterObservation with { RequirePersistentResponses = true });
        if (result.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(result.Error);
        var state = result.Value.Execution.Run ?? after;
        if (state.PlayerEntity is { } entity && result.Value.Execution.State.GetActor(entity.InstanceId) is { } actor)
            state = state with { PlayerEntity = entity with { Components = actor.Components } };
        return Result<ObservedGameplayExecution<RunState>>.Success(new(state, result.Value.Observation));
    }

    private static CombatState Snapshot(RunState run)
    {
        var actors = ImmutableDictionary<string, CombatActorState>.Empty.WithComparers(StringComparer.Ordinal);
        if (run.PlayerEntity is { } entity)
            actors = actors.Add(entity.InstanceId, new() { InstanceId = entity.InstanceId, DefinitionId = entity.DefinitionId,
                Name = entity.Name, ContentRevision = entity.ContentRevision, Components = entity.Components,
                SideId = "persistent.owner", ControllerBinding = new() { Kind = ControllerKind.Player } });
        return new() { CombatId = Guid.Empty, RunId = run.RunId, Determinism = run.Determinism, Actors = actors,
            Sides = [new() { SideId = "persistent.owner" }], Status = CombatStatus.ACTIVE };
    }
}
