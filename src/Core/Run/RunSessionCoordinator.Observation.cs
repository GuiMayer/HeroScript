using Core.Common;
using Core.GameplayEvents;

namespace Core.Run;

public sealed partial class RunSessionCoordinator
{
    /// <summary>Plans an isolated run command with its facts. Does not append, publish or populate receipts.</summary>
    public Result<ObservedGameplayExecution<RunTransitionPlan>> PlanObserved(Guid runId, GameplayCommandEnvelope command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var decoded = _codec.Decode(command);
        if (decoded.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(decoded.Error);
        if (decoded.Value.Descriptor.Route != GameplayCommandRoute.Run)
            return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure("Observation requires a run-only command");
        using var lease = _gates.Enter(runId);
        var loaded = _queries.GetRun(runId);
        if (loaded.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(loaded.Error);
        var state = loaded.Value;
        var identity = decoded.Value.Envelope.Identity;
        if (state.Sequence != identity.ExpectedSequence || state.Determinism.Step != identity.ExpectedStep)
            return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(
                RunCommandErrors.VersionConflict(identity.ExpectedSequence, identity.ExpectedStep, state));
        var handler = _handlers.Resolve(identity.Type);
        if (handler.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(handler.Error);
        var planned = PlanWithOccurrences(state, decoded.Value.Payload, identity, handler.Value);
        return planned.IsFailure ? Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(planned.Error)
            : Result<ObservedGameplayExecution<RunTransitionPlan>>.Success(new(planned.Value, planned.Value.Observation!));
    }

    private static Result<RunTransitionPlan> PlanWithOccurrences(RunState state, object payload, RunCommandIdentity identity, IRunCommandHandler handler)
    {
        var created = GameplayCommandObservation.Create(state, null, identity);
        if (created.IsFailure) return Result<RunTransitionPlan>.Failure(created.Error);
        var before = created.Value;
        var started = GameplayActionObservation.Begin(before, identity.Type, state.PlayerEntityId, "command.started", "command.input");
        if (started.IsFailure) return Result<RunTransitionPlan>.Failure(started.Error);
        var cursor = new GameplayRunObservation(started.Value);
        var planned = handler.Plan(state, payload, new(identity, state.Determinism) { Observation = cursor });
        if (planned.IsFailure) return Result<RunTransitionPlan>.Failure(planned.Error);
        if (planned.Value.Observation == null) return Result<RunTransitionPlan>.Failure("Run handler does not support occurrence execution");
        var completed = GameplayActionObservation.Complete(before, started.Value, planned.Value.Observation, identity.Type,
            state.PlayerEntityId, "command.completed", "command.input");
        return completed.IsFailure ? Result<RunTransitionPlan>.Failure(completed.Error)
            : Result<RunTransitionPlan>.Success(planned.Value with { Observation = completed.Value });
    }
}
