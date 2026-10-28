using System.Text.Json;
using Core.Common;
using Core.GameplayEvents;
using Core.Combat.Flow;

namespace Core.Run;

public sealed partial class RunManager
{
    /// <summary>The full encounter acknowledgement candidate, including cleanup, transport, exit activity and progression. No persistence.</summary>
    public Result<ObservedGameplayExecution<RunState>> PlanObservedResolution(Guid runId, Guid combatId,
        RunCommandIdentity identity, GameplayEventLimits? limits = null)
    {
        var loaded = GetRun(runId);
        if (loaded.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(loaded.Error);
        var run = loaded.Value;
        var encounter = run.GetActiveEncounter();
        if (_observation != null || identity.CommandId == Guid.Empty || identity.Type != RunCommandTypes.ResolveCombat ||
            identity.ExpectedSequence != run.Sequence || encounter == null || encounter.Combat.CombatId != combatId ||
            identity.ExpectedStep != encounter.Combat.Determinism.Step ||
            run.ResolvedMode?.CombatRules.Flow.EncounterResolution.Strategy is not null and not EncounterResolutionStrategy.ManualAck)
            return Result<ObservedGameplayExecution<RunState>>.Failure("Resolution identity, version, encounter or strategy does not match");
        var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision);
        if (catalog.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(catalog.Error);
        var transaction = GameplayEventTransaction.Create(new() { SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
            RunId = (run.Lineage?.RootRunId ?? runId).ToString("D"), BranchId = runId.ToString("D"), CombatId = combatId.ToString("D"),
            ActionId = identity.CommandId.ToString("D"), CommandId = identity.CommandId.ToString("D") }, catalog.Value, limits);
        if (transaction.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(transaction.Error);
        var before = new GameplayEffectObservation { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId,
            ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" };
        var started = GameplayActionObservation.Begin(before, identity.Type, run.PlayerEntityId, "command.started", "command.input");
        if (started.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(started.Error);
        var cursor = new GameplayRunObservation(started.Value);
        var resolved = ResolveEncounterCore(runId, run.Sequence, combatId, identity, default, cursor);
        if (resolved.IsFailure) return Result<ObservedGameplayExecution<RunState>>.Failure(resolved.Error);
        var completed = GameplayActionObservation.Complete(before, started.Value, cursor.Current, identity.Type,
            run.PlayerEntityId, "command.completed", "command.input");
        return completed.IsFailure ? Result<ObservedGameplayExecution<RunState>>.Failure(completed.Error)
            : Result<ObservedGameplayExecution<RunState>>.Success(new(resolved.Value, completed.Value));
    }

    private Result<RunState> FinishEncounterResolution(RunState previous, RunState candidate, object payload,
        RunCommandIdentity? identity, Guid combatId, GameplayRunObservation? observation, RunActivityEffectResult? diagnostics = null)
    {
        if (observation == null) return Persist(candidate, RunCommandTypes.ResolveCombat, payload, identity,
            scope: "combat", combatId: combatId, activityEffects: diagnostics);
        var normalized = PrepareCommitCandidate(candidate, previous);
        var ending = ObserveRunEnding(previous, normalized, observation);
        return ending.IsFailure ? Result<RunState>.Failure(ending.Error) : Result<RunState>.Success(normalized);
    }

    private static Result ObserveRunEnding(RunState? before, RunState after, GameplayRunObservation observation)
    {
        if (before?.Lifecycle != RunLifecycleState.Active || after.Lifecycle == RunLifecycleState.Active) return Result.Success();
        var observed = GameplayBoundaryObservation.Begin(observation.Current, GameplayBoundary.RunEnded, after.RunId.ToString("D"), null,
            new() { ContainerKind = "rule", ContainerId = "run.lifecycle", ComponentId = after.Lifecycle.ToString(),
                ContentRevision = after.Determinism.ContentRevision }, $"run.end/{observation.Current.Events.Occurrences.Count}");
        if (observed.IsFailure) return Result.Failure(observed.Error);
        observation.Accept(observed.Value with { ParentEventId = observation.Current.ParentEventId });
        return Result.Success();
    }
}
