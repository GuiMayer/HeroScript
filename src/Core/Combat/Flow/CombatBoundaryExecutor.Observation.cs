using Core.Common;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Determinism;
using Core.GameplayEvents;
using Core.Run;
using Core.Resources;
using Core.StatusEffects;
using Core.CardZones;
using Core.Effects;
using System.Collections.Immutable;

namespace Core.Combat.Flow;

public sealed partial class CombatBoundaryExecutor
{
    public Result<ObservedGameplayExecution<CombatInitializationResult>> InitializeObserved(RunState run, CombatState combat,
        PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition order,
        GameplayEffectObservation observation) => Observe(run, observation,
            cursor => InitializeTransactionCore(run, combat, sequence, policies, order, cursor));

    public Result<ObservedGameplayExecution<CombatFlowAdvanceResult>> AdvanceObserved(RunState run, CombatState combat,
        DeckState deck, DeterministicContext context, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition order, GameplayEffectObservation observation) => Observe(run, observation,
            cursor => AdvanceActivationCore(run, combat, deck, context, sequence, policies, order, cursor));

    public Result<ObservedGameplayExecution<CombatRelicLifecycleResult>> CompleteObserved(RunState run, CombatState combat,
        GameplayEffectObservation observation) => Observe(run, observation, cursor => CompleteCore(run, combat, cursor));

    private static Result<ObservedGameplayExecution<T>> Observe<T>(RunState run, GameplayEffectObservation observation,
        Func<ObservationCursor, Result<T>> execute)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision)
            return Result<ObservedGameplayExecution<T>>.Failure("Combat boundary observation requires the captured action revision");
        // This cursor is private to one call. Its values are immutable successors; neither cursor nor partial result escapes failure.
        var cursor = new ObservationCursor(observation);
        try
        {
            var result = execute(cursor);
            return result.IsFailure ? Result<ObservedGameplayExecution<T>>.Failure(result.Error)
                : Result<ObservedGameplayExecution<T>>.Success(new(result.Value, cursor.Current));
        }
        catch (ObservationFailure failure) { return Result<ObservedGameplayExecution<T>>.Failure(failure.Message); }
    }

    private sealed class ObservationFailure(string message) : Exception(message);

    private sealed class ObservationCursor(GameplayEffectObservation current)
    {
        public GameplayEffectObservation Current { get; private set; } = current;
        private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value : throw new ObservationFailure(result.Error);

        public IDisposable Boundary(GameplayBoundary boundary, string id, CombatState combat)
        {
            var parent = Current.ParentEventId;
            Current = Require(GameplayBoundaryObservation.Begin(Current, boundary, id, combat.ActivationState?.ActiveActorId,
                new() { ContainerKind = "rule", ContainerId = "combat.flow", ComponentId = id, ContentRevision = Current.Events.Scope.ContentRevision },
                $"combat.boundary/{Current.Events.Occurrences.Count}/{boundary}"));
            return new Frame(this, parent);
        }

        public Result<CombatStatusLifecycleResult> Status(ICombatStatusLifecycle lifecycle, RunState run, CombatState combat,
            StatusTriggerBoundary boundary, string? actorId)
        {
            var observed = Require(lifecycle.ProcessObserved(run, combat, boundary, Current, actorId));
            Current = observed.Observation;
            return Result<CombatStatusLifecycleResult>.Success(observed.Execution);
        }
        public Result<CombatRelicLifecycleResult> Relic(ICombatRelicLifecycle lifecycle, RunState run, CombatState combat, string boundary)
        {
            var observed = Require(lifecycle.ProcessObserved(run, combat, boundary, Current));
            Current = observed.Observation;
            return Result<CombatRelicLifecycleResult>.Success(observed.Execution);
        }
        public Result<CombatResourceLifecycleResult> Resource(ICombatResourceLifecycle lifecycle, RunState run, CombatState combat,
            string actorId, RegenerationTiming timing, IReadOnlySet<string>? excluded)
        {
            var observed = Require(lifecycle.ProcessObserved(run, combat, actorId, timing, Current, excluded));
            Current = observed.Observation;
            return Result<CombatResourceLifecycleResult>.Success(observed.Execution);
        }
        public Result<PhaseGraphResult> Phase(IPhaseGraphReducer phases, RunState run, CombatState combat, PhaseSequenceDefinition sequence, bool entering)
        {
            var observed = Require(entering ? phases.EnterObserved(run, combat, sequence, Current)
                : phases.ExitObserved(run, combat, sequence, Current));
            Current = observed.Observation;
            return Result<PhaseGraphResult>.Success(observed.Execution);
        }
        public void Cards(IReadOnlyList<CardZoneFlowStepRecord> steps, RunState before, RunState after, string? actorId) =>
            Current = Require(GameplayCardFlowObservation.Append(Current, steps, before, after, actorId));

        public void ModifierTicks(RunState before, RunState after)
        {
            var remaining = after.Modifiers.ToDictionary(item => item.InstanceId);
            foreach (var previous in before.Modifiers.OrderBy(item => item.InstanceId))
            {
                remaining.TryGetValue(previous.InstanceId, out var updated);
                if (updated != null && updated.Duration == previous.Duration) continue;
                var metadata = new GameplayOccurrenceDraft { ReasonId = Current.ReasonId, Attribution = GameplayAttribution.None,
                    Origin = new() { ContainerKind = "rule", ContainerId = "modifier.lifecycle", ComponentId = previous.InstanceId.ToString("D"),
                        ContentRevision = after.Determinism.ContentRevision } };
                var fact = updated == null ? Require(GameplayRuleFactProducer.Modifier(new()
                {
                    Store = EffectStackStore.Modifier, InstanceId = previous.InstanceId, DefinitionId = previous.ModifierId,
                    Owner = previous.Owner, PreviousStacks = previous.Stacks, CurrentStacks = 0, Reason = EffectStackChangeReason.Expire
                }, previous, null, metadata)) : Require(GameplayRuleFactProducer.ModifierDuration(previous, updated, metadata));
                var appended = Require(Current.Events.Append(Current.PublisherId, $"modifier.lifecycle/{Current.Events.Occurrences.Count}", fact, Current.ParentEventId));
                Current = Current with { Events = appended.Transaction };
            }
        }

        public void ResourceMutation(ResolvedResourceMutation mutation, ResourceMutationRecord record, CombatActorState before, CombatActorState after)
        {
            var metadata = new GameplayOccurrenceDraft { ReasonId = Current.ReasonId, Attribution = GameplayAttribution.None,
                SubjectActorId = after.InstanceId, Origin = new() { ContainerKind = "rule", ContainerId = "resource-cycle",
                    ComponentId = mutation.ResourceId, ContentRevision = Current.Events.Scope.ContentRevision } };
            var fact = Require(GameplayResourceFactProducer.FromMutation(mutation, record,
                new() { Kind = "entity", Id = after.InstanceId, ScopeId = Current.Events.Scope.CombatId ?? Current.Events.Scope.RunId }, metadata));
            var appended = Require(Current.Events.Append(Current.PublisherId, $"resource-cycle/{Current.Events.Occurrences.Count}", fact, Current.ParentEventId));
            Current = Current with { Events = appended.Transaction };
            var rules = Require(GameplayRuleFactProducer.ResourceRules(new EffectResourceOutcome
            {
                PreviousValue = record.PreviousValue, CurrentValue = record.CurrentValue, RequestedValue = record.RequestedValue,
                OwnerDefeatedBefore = !before.IsAlive, OwnerDefeatedAfter = !after.IsAlive,
                ThresholdFacts = after.ResourceState.Resources.Values.OrderBy(pool => pool.ResourceId, StringComparer.Ordinal)
                    .Select(ResourceThresholdEvaluator.Resolve).Where(item => item != null).Cast<ResourceThresholdFact>().ToImmutableArray()
            }, metadata));
            foreach (var rule in rules)
            {
                var emitted = Require(Current.Events.Append(Current.PublisherId, $"resource-cycle/rules/{Current.Events.Occurrences.Count}", rule,
                    appended.Occurrence.EventId));
                Current = Current with { Events = emitted.Transaction };
            }
        }

        private sealed class Frame(ObservationCursor cursor, string? parent) : IDisposable
        {
            public void Dispose() => cursor.Current = cursor.Current with { ParentEventId = parent };
        }
    }
}
