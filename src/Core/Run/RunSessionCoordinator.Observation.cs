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
        var catalog = GameplayCoreCatalog.Create(state.SettingId, state.Determinism.ContentRevision);
        if (catalog.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(catalog.Error);
        var transaction = GameplayEventTransaction.Create(new()
        {
            SettingId = state.SettingId, ContentRevision = state.Determinism.ContentRevision,
            RunId = (state.Lineage?.RootRunId ?? state.RunId).ToString("D"), BranchId = state.RunId.ToString("D"),
            ActionId = identity.CommandId.ToString("D"), CommandId = identity.CommandId.ToString("D")
        }, catalog.Value);
        if (transaction.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(transaction.Error);
        var before = new GameplayEffectObservation { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId,
            ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" };
        var started = GameplayActionObservation.Begin(before, identity.Type, state.PlayerEntityId, "command.started", "command.input");
        if (started.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(started.Error);
        var cursor = new GameplayRunObservation(started.Value);
        var planned = handler.Value.Plan(state, decoded.Value.Payload, new(identity, state.Determinism) { Observation = cursor });
        if (planned.IsFailure) return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(planned.Error);
        // A handler must explicitly opt into the isolated observed contract.
        if (planned.Value.Observation == null)
            return Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure("Run handler does not support observation");
        var completed = GameplayActionObservation.Complete(before, started.Value, planned.Value.Observation, identity.Type,
            state.PlayerEntityId, "command.completed", "command.input");
        return completed.IsFailure ? Result<ObservedGameplayExecution<RunTransitionPlan>>.Failure(completed.Error)
            : Result<ObservedGameplayExecution<RunTransitionPlan>>.Success(new(planned.Value with { Observation = completed.Value }, completed.Value));
    }
}
