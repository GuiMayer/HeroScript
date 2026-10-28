using Core.Common;
using Core.GameplayEvents;
using Core.Run.Branching;

namespace Core.Run;

public sealed partial class RunManager
{
    /// <summary>Pure creation candidate from explicit deterministic inputs. Reads content/profile inputs but never stores a run or receipt.</summary>
    public Result<ObservedGameplayExecution<RunState>> PlanObservedCreation(RunStartOptions options, GameplayEventLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_observation != null || options.Seed == null || options.CreationCommandId is null || options.CreationCommandId == Guid.Empty ||
            _repository != null && (options.CapturedEligibility != null || options.CapturedCreationIdentity != null))
            return Result<ObservedGameplayExecution<RunState>>.Failure("Observed creation requires an explicit seed, command identity and trusted captured inputs");
        try
        {
            var planning = new CreationObservation(limits);
            var identity = options.CapturedCreationIdentity ?? RunCreationService.Identify(options);
            var result = StartRunCore(options, identity, planning);
            return result.IsFailure ? Result<ObservedGameplayExecution<RunState>>.Failure(result.Error)
                : Result<ObservedGameplayExecution<RunState>>.Success(new(result.Value, planning.Current!.Current));
        }
        catch (Exception exception)
        {
            return Result<ObservedGameplayExecution<RunState>>.Failure($"Observed creation failed: {exception.Message}", exception);
        }
    }

    private static RunState PrepareCommitCandidate(RunState state, RunState? previous) =>
        DeckCompartments.DeckCompartmentPlanner.ReconcileDestroyedCards(PersistentPlayerTransitions.ApplyResourceConsequences(state)) with
        {
            Sequence = checked((previous?.Sequence ?? 0) + 1),
            Lineage = previous == null ? state.Lineage ?? RunLineage.Root(state.RunId) : previous.Lineage
        };

    private sealed class CreationObservation(GameplayEventLimits? limits)
    {
        public GameplayRunObservation? Current { get; private set; }
        private GameplayEffectObservation _before = null!;
        private GameplayEffectObservation _started = null!;

        public Result Begin(RunState state, Guid commandId, IReadOnlyList<Core.CardZones.CardZoneFlowStepRecord> initialSteps)
        {
            var catalog = GameplayCoreCatalog.Create(state.SettingId, state.Determinism.ContentRevision);
            if (catalog.IsFailure) return Result.Failure(catalog.Error);
            var transaction = GameplayEventTransaction.Create(new() { SettingId = state.SettingId,
                ContentRevision = state.Determinism.ContentRevision, RunId = state.RunId.ToString("D"), BranchId = state.RunId.ToString("D"),
                ActionId = commandId.ToString("D"), CommandId = commandId.ToString("D") }, catalog.Value, limits);
            if (transaction.IsFailure) return Result.Failure(transaction.Error);
            _before = new() { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId, ReasonId = "effect.application",
                CostReasonId = "cost.payment", RefundReasonId = "cost.refund" };
            var started = GameplayActionObservation.Begin(_before, RunCommandTypes.StartRun, state.PlayerEntityId, "command.started", "command.input");
            if (started.IsFailure) return Result.Failure(started.Error);
            _started = started.Value;
            var boundary = GameplayBoundaryObservation.Begin(_started, GameplayBoundary.RunStarted, state.RunId.ToString("D"), null,
                new() { ContainerKind = "rule", ContainerId = "run.lifecycle", ComponentId = "creation",
                    ContentRevision = state.Determinism.ContentRevision }, "run.start");
            if (boundary.IsFailure) return Result.Failure(boundary.Error);
            // Bootstrap declarations are the genesis baseline. These records describe only the authored startup flows.
            var flowed = GameplayCardFlowObservation.Append(boundary.Value, initialSteps, state, state, state.PlayerEntityId);
            if (flowed.IsFailure) return Result.Failure(flowed.Error);
            Current = new(flowed.Value);
            return Result.Success();
        }

        public Result Complete(RunState state)
        {
            if (state.Lifecycle != RunLifecycleState.Active)
            {
                var ended = GameplayBoundaryObservation.Begin(Current!.Current, GameplayBoundary.RunEnded, state.RunId.ToString("D"), null,
                    new() { ContainerKind = "rule", ContainerId = "run.lifecycle", ComponentId = state.Lifecycle.ToString(),
                        ContentRevision = state.Determinism.ContentRevision }, "run.end");
                if (ended.IsFailure) return Result.Failure(ended.Error);
                Current.Accept(ended.Value);
            }
            var completed = GameplayActionObservation.Complete(_before, _started, Current!.Current, RunCommandTypes.StartRun,
                state.PlayerEntityId, "command.completed", "command.input");
            if (completed.IsFailure) return Result.Failure(completed.Error);
            Current.Accept(completed.Value);
            return Result.Success();
        }
    }
}
