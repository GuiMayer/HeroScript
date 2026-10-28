using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;

namespace Core.Effects;

public sealed partial class EffectTriggerExecutor
{
    public Result<IGameplayBoundaryExecutionScope> BeginBoundaryScope(RunState run, CombatState combat, GameplayEffectObservation? observation = null)
    {
        if (run == null || combat == null || observation != null && observation.Events == null)
            return Result<IGameplayBoundaryExecutionScope>.Failure("Boundary execution requires immutable run/combat snapshots and a valid observation");
        var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision);
        if (catalog.IsFailure) return Result<IGameplayBoundaryExecutionScope>.Failure(catalog.Error);
        if (observation != null && (observation.Events.Scope.SettingId != run.SettingId || observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision ||
            observation.Events.Scope.RunId != (run.Lineage?.RootRunId ?? run.RunId).ToString("D") || observation.Events.Scope.BranchId != run.RunId.ToString("D")))
            return Result<IGameplayBoundaryExecutionScope>.Failure("Boundary observation must use its actual pinned run scope");
        var expose = observation != null;
        if (observation == null)
        {
            var id = CanonicalJson.ComputeHash(new { run.RunId, run.Sequence, combat, boundaryScope = true });
            var events = GameplayEventTransaction.Create(new() { SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
                RunId = (run.Lineage?.RootRunId ?? run.RunId).ToString("D"), BranchId = run.RunId.ToString("D"), CombatId = combat.CombatId.ToString("D"),
                ActionId = id, CommandId = id }, catalog.Value);
            if (events.IsFailure) return Result<IGameplayBoundaryExecutionScope>.Failure(events.Error);
            observation = new() { Events = events.Value, PublisherId = GameplayCoreCatalog.PublisherId,
                ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" };
        }
        var quotas = GameplayResponseQuotas.Begin(observation.Events.Scope);
        return quotas.IsFailure ? Result<IGameplayBoundaryExecutionScope>.Failure(quotas.Error)
            : Result<IGameplayBoundaryExecutionScope>.Success(new BoundaryExecutionScope(this, catalog.Value, observation, quotas.Value, expose));
    }

    private sealed class BoundaryExecutionScope(EffectTriggerExecutor executor, GameplayEventCatalog catalog,
        GameplayEffectObservation initial, GameplayResponseQuotas initialQuotas, bool expose) : IGameplayBoundaryExecutionScope
    {
        private readonly ExecutionBudget _budget = new();
        private GameplayEffectObservation _observation = initial;
        private readonly GameplayEventScope _scope = initial.Events.Scope;
        private GameplayResponseQuotas _quotas = initialQuotas;
        private readonly Dictionary<(string?, string, string, string?), string> _boundaries = new();
        private bool _failed;

        public Result<GameplayBoundaryDispatchResult> Dispatch(RunState run, CombatState combat, string kind, string boundaryId,
            string? actorId, IReadOnlyList<GameplayBoundaryInvocation> invocations, GameplayEffectObservation? observation = null)
        {
            Result<GameplayBoundaryDispatchResult> Fail(string error) { _failed = true; return Result<GameplayBoundaryDispatchResult>.Failure(error); }
            if (_failed || run == null || combat == null || run.SettingId != catalog.SettingId || run.Determinism.ContentRevision != catalog.ContentRevision ||
                run.RunId.ToString("D") != _scope.BranchId || invocations == null || invocations.Count > 4096 || (observation != null) != expose)
                return Fail("Boundary execution scope is failed, invalid or belongs to another run");
            var current = observation ?? _observation;
            if (current.Events.Scope != _scope || current.Events.Occurrences.Count < _observation.Events.Occurrences.Count)
                return Fail("Boundary execution cannot replace or rewind its action context");
            if ((run.Lineage?.RootRunId ?? run.RunId).ToString("D") != _scope.RunId ||
                _scope.CombatId != null && combat.CombatId.ToString("D") != _scope.CombatId ||
                !ReferenceEquals(current.Events, _observation.Events) && _observation.Events.Occurrences.Where((item, index) => !ReferenceEquals(item, current.Events.Occurrences[index])).Any())
                return Fail("Boundary execution cannot substitute its run, combat or causal prefix");
            var parentId = current.ParentEventId;
            var key = (parentId, kind, boundaryId, actorId);
            var fact = parentId == null ? null : current.Events.Occurrences.FirstOrDefault(item => item.EventId == parentId && item.Kind == kind && item.Payload is BoundaryOccurrencePayload);
            if (fact == null && _boundaries.TryGetValue(key, out var existing)) fact = current.Events.Occurrences.Single(item => item.EventId == existing);
            if (fact == null)
            {
                var appended = current.Events.Append(current.PublisherId, $"boundary/{current.Events.Occurrences.Count}", new()
                { Kind = kind, ReasonId = current.ReasonId, Attribution = GameplayAttribution.None, SubjectActorId = actorId,
                    Payload = new BoundaryOccurrencePayload { BoundaryId = boundaryId, Sequence = current.Events.Occurrences.Count } }, parentId);
                if (appended.IsFailure) return Fail(appended.Error);
                current = current with { Events = appended.Value.Transaction };
                fact = appended.Value.Occurrence; _boundaries[key] = fact.EventId;
            }
            // The timing owner already chose the boundary. The migration view retains its authored label without emitting a duplicate fact.
            var matchingFact = fact with { Payload = new BoundaryOccurrencePayload { BoundaryId = boundaryId, Sequence = ((BoundaryOccurrencePayload)fact.Payload).Sequence } };
            _budget.Responses = new(catalog, [new CapturedBoundaryObserverProvider(invocations.ToImmutableArray()), new LegacyResourceReactionObserverProvider()], _quotas,
                new GameplayResponseEvaluation(executor._formulas, executor._contentRuntimes), null, null);
            Result<EffectBatchResult?> executed;
            try { executed = executor.DispatchFacts(_budget, current, [matchingFact], combat, run, combat, run, 0, null); }
            catch (GameplayQueryException exception) { return Fail(exception.Message); }
            if (executed.IsFailure) return Fail(executed.Error);
            var result = executed.Value!;
            _quotas = _budget.Responses.Quotas;
            _observation = result.Observation! with { ParentEventId = parentId };
            return Result<GameplayBoundaryDispatchResult>.Success(new(result, expose ? _observation : null, result.BoundaryInvocations));
        }
    }
}
