using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;

namespace Core.Effects;

public sealed record ResponsiveEffectExecution(EffectBatchResult Execution, GameplayEffectObservation Observation, GameplayResponseQuotas Quotas);

public sealed partial class EffectTriggerExecutor
{
    /// <summary>Explicit candidate execution. Does not publish content, persist a run or enable responses in other entry points.</summary>
    public Result<ResponsiveEffectExecution> ExecuteResponsiveObserved(EffectTriggerExecutionRequest request,
        GameplayEffectObservation observation, GameplayEventCatalog catalog, IReadOnlyList<IGameplayObserverProvider> providers,
        int maximumInvocations = GameplayResponseQuotas.MaximumInvocations, Guid? selectedCardId = null, Guid? resolvingCardId = null)
    {
        if (request?.Run == null || observation?.Events == null || catalog == null || providers == null || providers.Count > 64 ||
            request.Run.SettingId != catalog.SettingId || request.Run.Determinism.ContentRevision != catalog.ContentRevision ||
            observation.Events.Scope.SettingId != catalog.SettingId || observation.Events.Scope.ContentRevision != catalog.ContentRevision ||
            observation.Events.Scope.RunId != (request.Run.Lineage?.RootRunId ?? request.Run.RunId).ToString("D") ||
            observation.Events.Scope.BranchId != request.Run.RunId.ToString("D") ||
            providers.Any(provider => provider == null || !EventValue.Valid(provider.ProviderId)) ||
            providers.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count() != providers.Count)
            return Result<ResponsiveEffectExecution>.Failure("Responsive execution requires a pinned run, catalog and observation");
        var quotas = GameplayResponseQuotas.Begin(observation.Events.Scope, maximumInvocations);
        if (quotas.IsFailure) return Result<ResponsiveEffectExecution>.Failure(quotas.Error);
        var session = new ResponseExecutionSession(catalog, providers.ToImmutableArray(), quotas.Value,
            new GameplayResponseEvaluation(_formulas, _contentRuntimes), selectedCardId, resolvingCardId);
        try
        {
            var result = Execute(request, null, new ExecutionBudget { Responses = session }, suppressReactions: false, observation: observation);
            return result.IsFailure ? Result<ResponsiveEffectExecution>.Failure(result.Error)
                : Result<ResponsiveEffectExecution>.Success(new(result.Value, result.Value.Observation!, session.Quotas));
        }
        catch (GameplayQueryException exception) { return Result<ResponsiveEffectExecution>.Failure(exception.Message); }
    }

    private sealed class ResponseExecutionSession(GameplayEventCatalog catalog, ImmutableArray<IGameplayObserverProvider> providers,
        GameplayResponseQuotas quotas, GameplayResponseEvaluation evaluation, Guid? selectedCardId, Guid? resolvingCardId)
    {
        internal GameplayResponseQuotas Quotas = quotas;
        internal GameplayObserverCollectionContext Snapshot(RunState run, CombatState combat, ResponseSnapshot snapshot) => new()
        { Run = run, Combat = combat, Catalog = catalog, Snapshot = snapshot, SelectedCardId = selectedCardId, ResolvingCardId = resolvingCardId };

        internal Result<ImmutableArray<GameplayResponseCandidate>> Freeze(ImmutableArray<GameplayOccurrence> facts,
            RunState runBefore, CombatState combatBefore, RunState runAfter, CombatState combatAfter)
        {
            var candidates = ImmutableArray.CreateBuilder<GameplayResponseCandidate>();
            foreach (var snapshot in new[] { Snapshot(runBefore, combatBefore, ResponseSnapshot.BeforeOccurrence),
                         Snapshot(runAfter, combatAfter, ResponseSnapshot.AfterOccurrence) })
            {
                var registrations = GameplayObserverCollector.Collect(snapshot, providers);
                if (registrations.IsFailure) return Result<ImmutableArray<GameplayResponseCandidate>>.Failure(registrations.Error);
                var matcher = GameplayResponseMatcher.Create(registrations.Value);
                if (matcher.IsFailure) return Result<ImmutableArray<GameplayResponseCandidate>>.Failure(matcher.Error);
                foreach (var fact in facts)
                {
                    var matches = matcher.Value.Match(fact, snapshot);
                    if (matches.IsFailure) return Result<ImmutableArray<GameplayResponseCandidate>>.Failure(matches.Error);
                    foreach (var candidate in matches.Value)
                    {
                        var condition = evaluation.Condition(candidate);
                        if (condition.IsFailure) return Result<ImmutableArray<GameplayResponseCandidate>>.Failure(condition.Error);
                        if (!condition.Value) continue;
                        if (candidates.Count == GameplayResponseQuotas.MaximumInvocations)
                            return Result<ImmutableArray<GameplayResponseCandidate>>.Failure("Response scheduling budget exceeded");
                        candidates.Add(candidate);
                    }
                }
            }
            return Result<ImmutableArray<GameplayResponseCandidate>>.Success(candidates.OrderBy(item => item.Occurrence.Ordinal)
                .ThenBy(item => item.Registration.Order).ToImmutableArray());
        }

        internal Result<bool> Revalidate(GameplayResponseCandidate candidate, RunState run, CombatState combat)
        {
            var availability = candidate.Registration.Response.Definition.Availability;
            var snapshot = Snapshot(run, combat, availability.Snapshot);
            if (availability.RevalidateOwner && (!snapshot.ContainsActor(candidate.OwnerActorId) ||
                availability.RequireLivingOwner && !GameplayResponseMatcher.Alive(candidate.OwnerActorId, snapshot))) return Result<bool>.Success(false);
            if (!availability.RequireRegistrationStillActive) return Result<bool>.Success(true);
            var registrations = GameplayObserverCollector.Collect(snapshot, providers);
            return registrations.IsFailure ? Result<bool>.Failure(registrations.Error)
                : Result<bool>.Success(registrations.Value.Any(item => item.RegistrationId == candidate.Registration.RegistrationId));
        }

        internal Result<ImmutableDictionary<string, float>> Variables(GameplayResponseCandidate candidate) => evaluation.EffectVariables(candidate);
    }

    private Result<EffectBatchResult?> DispatchResponses(ExecutionBudget budget, bool enabled, GameplayEffectObservation? observation,
        int firstFact, CombatState before, RunState? runBefore, CombatState after, RunState? runAfter, int depth)
    {
        if (!enabled || budget.Responses is not { } session || observation == null || observation.Events.Occurrences.Count == firstFact)
            return Result<EffectBatchResult?>.Success(null);
        if (runBefore == null || runAfter == null) return Result<EffectBatchResult?>.Failure("Response execution lost its run snapshot");
        var facts = observation.Events.Occurrences.Skip(firstFact).ToImmutableArray();
        var frozen = session.Freeze(facts, runBefore, before, runAfter, after);
        if (frozen.IsFailure) return Result<EffectBatchResult?>.Failure(frozen.Error);
        var state = after;
        var run = runAfter;
        var currentObservation = observation;
        var records = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        var calculations = ImmutableArray.CreateBuilder<Core.Calculations.CalculationResult>();
        var steps = ImmutableArray.CreateBuilder<EffectExecutionStep>();
        foreach (var candidate in frozen.Value)
        {
            var revalidated = session.Revalidate(candidate, run, state);
            if (revalidated.IsFailure) return Result<EffectBatchResult?>.Failure(revalidated.Error);
            if (!revalidated.Value) continue;
            var reserved = session.Quotas.Reserve(candidate);
            if (reserved.IsFailure) return Result<EffectBatchResult?>.Failure(reserved.Error);
            if (!reserved.Value.Accepted) continue;
            session.Quotas = reserved.Value.Quotas;
            var variables = session.Variables(candidate);
            if (variables.IsFailure) return Result<EffectBatchResult?>.Failure(variables.Error);
            var definition = candidate.Registration.Response.Definition;
            var request = new EffectTriggerExecutionRequest
            {
                Combat = state, Run = run, OwnerEntityId = candidate.OwnerActorId, SourceEntityId = candidate.SourceActorId,
                ContentRevision = candidate.Registration.Origin.ContentRevision, SelectedTargetEntityIds = [candidate.TargetActorId],
                Trigger = new() { TriggerId = reserved.Value.InvocationId!, Effects = definition.Effects },
                Variables = variables.Value, Provenance = new() { Kind = EffectProvenanceKind.Rule,
                    SourceId = candidate.Registration.RegistrationId, ComponentId = definition.ResponseId }
            };
            var childObservation = currentObservation with { ParentEventId = candidate.Occurrence.EventId,
                ReasonId = definition.ReasonId ?? observation.ReasonId, ResponseOrigin = candidate.Registration.Origin };
            var result = Execute(request, null, budget, suppressReactions: true, baseDepth: depth + 1,
                observation: childObservation, dispatchResponses: definition.Propagation == ResponsePropagation.AllowResponses);
            if (result.IsFailure) return Result<EffectBatchResult?>.Failure(result.Error);
            state = result.Value.State;
            run = result.Value.Run!;
            currentObservation = result.Value.Observation! with { ParentEventId = observation.ParentEventId,
                ReasonId = observation.ReasonId, LastApplicationEventId = observation.LastApplicationEventId, ResponseOrigin = observation.ResponseOrigin };
            records.AddRange(result.Value.Records); calculations.AddRange(result.Value.Calculations);
            foreach (var step in result.Value.Steps) steps.Add(step with { Index = steps.Count });
        }
        return Result<EffectBatchResult?>.Success(new() { State = state, Run = run, Observation = currentObservation,
            Records = records.ToImmutable(), Calculations = calculations.ToImmutable(), Steps = steps.ToImmutable() });
    }
}
