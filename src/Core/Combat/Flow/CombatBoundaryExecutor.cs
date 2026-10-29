using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Combat.TurnOrder;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Calculations;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Core.CardZones;
using Core.GameplayEvents;

namespace Core.Combat.Flow;

public sealed record CombatFlowAdvanceResult
{
    private ImmutableArray<CombatResolutionStep> _steps = [];

    public IReadOnlyList<CombatResolutionStep> Steps
    {
        get => _steps;
        init => _steps = value?.ToImmutableArray() ?? [];
    }

    public CombatState Combat => _steps[^1].Combat;
    public DeckState Deck => _steps[^1].Deck;
}

public interface ICombatFlowPlanner
{
    Result<CombatState> Initialize(RunState run, CombatState combat);
    Result<CombatInitializationResult> InitializeTransaction(RunState run, CombatState combat);
    Result<CombatRelicLifecycleResult> Complete(RunState run, CombatState combat);

    Result<CombatFlowAdvanceResult> AdvanceActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism);
    Result<ObservedGameplayExecution<CombatInitializationResult>> InitializeObserved(RunState run, CombatState combat, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatInitializationResult>>.Failure("This flow does not support gameplay observation");
    Result<ObservedGameplayExecution<CombatFlowAdvanceResult>> AdvanceObserved(RunState run, CombatState combat, DeckState deck, DeterministicContext context, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatFlowAdvanceResult>>.Failure("This flow does not support gameplay observation");
    Result<ObservedGameplayExecution<CombatRelicLifecycleResult>> CompleteObserved(RunState run, CombatState combat, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatRelicLifecycleResult>>.Failure("This flow does not support gameplay observation");

}

public interface ICombatBoundaryExecutor
{
    Result<CombatInitializationResult> InitializeTransaction(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy);

    Result<CombatRelicLifecycleResult> Complete(RunState run, CombatState combat);

    Result<CombatFlowAdvanceResult> AdvanceActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy);
    Result<ObservedGameplayExecution<CombatInitializationResult>> InitializeObserved(RunState run, CombatState combat, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition order, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatInitializationResult>>.Failure("This flow does not support gameplay observation");
    Result<ObservedGameplayExecution<CombatFlowAdvanceResult>> AdvanceObserved(RunState run, CombatState combat, DeckState deck, DeterministicContext context, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition order, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatFlowAdvanceResult>>.Failure("This flow does not support gameplay observation");
    Result<ObservedGameplayExecution<CombatRelicLifecycleResult>> CompleteObserved(RunState run, CombatState combat, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatRelicLifecycleResult>>.Failure("This flow does not support gameplay observation");

}

/// <summary>
/// Orders configured combat lifecycle boundaries and produces immutable frames.
/// It owns no persistence, automatic-controller loop or runtime publication.
/// </summary>
public sealed partial class CombatBoundaryExecutor : ICombatBoundaryExecutor
{
    private readonly ITurnOrderResolver _turnOrder;
    private readonly ICombatStatusLifecycle _statusLifecycle;
    private readonly ICombatRelicLifecycle _relicLifecycle;
    private readonly ICombatResourceLifecycle _resourceLifecycle;
    private readonly IPhaseGraphReducer _phases;
    private readonly ICombatOutcomeResolver _outcomes;
    private readonly ICardZoneFlowExecutor? _cardZoneFlows;
    private readonly IActivationExitGuard? _exitGuard;

    public CombatBoundaryExecutor(
        ITurnOrderResolver turnOrder,
        ICombatStatusLifecycle statusLifecycle,
        ICombatRelicLifecycle relicLifecycle,
        ICombatResourceLifecycle resourceLifecycle,
        IPhaseGraphReducer phases,
        ICombatOutcomeResolver outcomes,
        ICardZoneFlowExecutor? cardZoneFlows = null,
        IActivationExitGuard? exitGuard = null, IGameplayOccurrenceExecutor? occurrences = null)
    {
        _turnOrder = turnOrder ?? throw new ArgumentNullException(nameof(turnOrder));
        _statusLifecycle = statusLifecycle ?? throw new ArgumentNullException(nameof(statusLifecycle));
        _relicLifecycle = relicLifecycle ?? throw new ArgumentNullException(nameof(relicLifecycle));
        _resourceLifecycle = resourceLifecycle ?? throw new ArgumentNullException(nameof(resourceLifecycle));
        _phases = phases ?? throw new ArgumentNullException(nameof(phases));
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
        _cardZoneFlows = cardZoneFlows;
        _exitGuard = exitGuard;
        _occurrences = occurrences;
    }

    public Result<CombatInitializationResult> InitializeTransaction(RunState run, CombatState combat, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition turnOrderPolicy) => WithOccurrenceObservation(run, combat, cursor => InitializeTransactionCore(run, combat, sequence, policies, turnOrderPolicy, cursor));

    private Result<CombatInitializationResult> InitializeTransactionCore(RunState run, CombatState combat, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition turnOrderPolicy, ObservationCursor? observation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(turnOrderPolicy);
        using var combatBoundary = observation?.Boundary(Core.GameplayEvents.GameplayBoundary.CombatStarted, "combat.started", combat);
        var provisioned = CardZoneRunFlowDispatcher.EnsureParticipants(run,
            combat.GetAllActors().Select(actor => actor.InstanceId).ToArray());
        if (provisioned.IsFailure) return Result<CombatInitializationResult>.Failure(provisioned.Error);
        if (!ReferenceEquals(provisioned.Value.State, run.Deck.Topology))
            run = run with { Deck = new DeckState { Topology = provisioned.Value.State } };
        var effectSteps = new List<EffectExecutionStep>();
        var applications = new List<EffectApplicationRecord>();
        var phaseTransitions = new List<PhaseTransitionRecord>();
        var cardZoneSteps = new List<CardZoneFlowStepRecord>();
        var openingResponses = BoundaryResponses(observation, run, combat);
        run = openingResponses.Run ?? run; combat = openingResponses.State;
        effectSteps.AddRange(openingResponses.Steps); applications.AddRange(openingResponses.Records);
        var flowed = CardZoneRunFlowDispatcher.Execute(_cardZoneFlows, run, run.Deck,
            run.Determinism, "encounter.started", combatSnapshot: combat);
        if (flowed.IsFailure) return Result<CombatInitializationResult>.Failure(flowed.Error);
        observation?.Cards(flowed.Value.Steps, run, run with { Deck = new DeckState { Topology = flowed.Value.State } }, null);
        cardZoneSteps.AddRange(flowed.Value.Steps);
        run = run with
        {
            Deck = new DeckState { Topology = flowed.Value.State },
            Determinism = flowed.Value.Context
        };
        var initialized = InitializeActivation(
            run,
            combat,
            sequence,
            policies,
            turnOrderPolicy,
            _turnOrder, observation);
        if (initialized.IsFailure) return Result<CombatInitializationResult>.Failure(initialized.Error);
        run = initialized.Value.Run;
        effectSteps.AddRange(initialized.Value.EffectSteps); applications.AddRange(initialized.Value.Applications);
        var regenerated = ApplyResourceLifecycle(
            run,
            initialized.Value.Combat,
            initialized.Value.Combat.ActivationState!.ActiveActorId!,
            RegenerationTiming.START_TURN,
            policies, observation);
        if (regenerated.IsFailure) return Result<CombatInitializationResult>.Failure(regenerated.Error);
        run = regenerated.Value.Run ?? run;
        effectSteps.AddRange(regenerated.Value.Steps);
        applications.AddRange(regenerated.Value.Records);
        var relics = (observation == null ? _relicLifecycle.Process(run, regenerated.Value.Combat, CombatTriggerBoundaries.CombatStart)
            : observation.Relic(_relicLifecycle, run, regenerated.Value.Combat, CombatTriggerBoundaries.CombatStart));
        if (relics.IsFailure) return Result<CombatInitializationResult>.Failure(relics.Error);
        run = relics.Value.Run ?? run;
        effectSteps.AddRange(relics.Value.Events.SelectMany(item => item.Steps));
        applications.AddRange(relics.Value.Events.SelectMany(item => item.Applications));
        var afterRelics = _outcomes.Evaluate(
            relics.Value.Combat,
            policies.Outcome,
            initialized.Value.Combat.ActivationState?.ActiveActorId,
            CombatOutcomeEvaluationPoint.LifecycleBoundary);
        var initial = ApplyInitialLifecycle(run, afterRelics, policies, observation);
        if (initial.IsFailure) return initial;
        run = initial.Value.Run;
        effectSteps.AddRange(initial.Value.EffectSteps);
        applications.AddRange(initial.Value.Applications);
        var current = initial.Value.Combat;
        if (current.IsActive)
        {
            var entered = observation == null ? _phases.Enter(run, current, sequence) : observation.Phase(_phases, run, current, sequence, true);
            if (entered.IsFailure) return Result<CombatInitializationResult>.Failure(entered.Error);
            run = entered.Value.Run;
            current = _outcomes.Evaluate(
                entered.Value.Combat,
                policies.Outcome,
                entered.Value.Combat.ActivationState?.ActiveActorId,
                CombatOutcomeEvaluationPoint.LifecycleBoundary);
            effectSteps.AddRange(entered.Value.Steps);
            applications.AddRange(entered.Value.Applications);
            phaseTransitions.AddRange(entered.Value.Transitions);
        }
        if (!current.IsActive)
        {
            var completed = CompleteCore(run, current, observation);
            if (completed.IsFailure) return Result<CombatInitializationResult>.Failure(completed.Error);
            effectSteps.AddRange(completed.Value.Events.SelectMany(item => item.Steps).Concat(completed.Value.ResponseSteps));
            applications.AddRange(completed.Value.Events.SelectMany(item => item.Applications).Concat(completed.Value.ResponseApplications));
            return Result<CombatInitializationResult>.Success(CreateInitializationResult(
                completed.Value.Combat,
                completed.Value.Run ?? run,
                effectSteps,
                applications,
                phaseTransitions,
                cardZoneSteps));
        }
        return Result<CombatInitializationResult>.Success(CreateInitializationResult(
            current,
            run,
            effectSteps,
            applications,
            phaseTransitions,
            cardZoneSteps));
    }

    public Result<CombatRelicLifecycleResult> Complete(RunState run, CombatState combat) => WithOccurrenceObservation(run, combat, cursor => CompleteCore(run, combat, cursor));

    private Result<CombatRelicLifecycleResult> CompleteCore(RunState run, CombatState combat, ObservationCursor? observation)
    {
        if (combat.IsActive) return Result<CombatRelicLifecycleResult>.Failure("Cannot finalize an active combat");
        using var combatBoundary = combat.CompletedLifecycleBoundaries.Contains(CombatTriggerBoundaries.CombatEnd) ? null
            : observation?.Boundary(Core.GameplayEvents.GameplayBoundary.CombatEnded, "combat.ended", combat);
        var terminalResponses = combat.CompletedLifecycleBoundaries.Contains(CombatTriggerBoundaries.CombatEnd)
            ? new EffectBatchResult { State = combat, Run = run } : BoundaryResponses(observation, run, combat);
        run = terminalResponses.Run ?? run; combat = terminalResponses.State;
        var result = (observation == null ? _relicLifecycle.Process(run, combat, CombatTriggerBoundaries.CombatEnd)
            : observation.Relic(_relicLifecycle, run, combat, CombatTriggerBoundaries.CombatEnd));
        if (result.IsFailure || combat.CompletedLifecycleBoundaries.Contains(CombatTriggerBoundaries.CombatEnd)) return result;
        var updatedRun = Core.Combat.Modifiers.ModifierTransitions.Tick(result.Value.Run ?? run,
            Core.Combat.Modifiers.ModifierDurationBoundary.Combat, eligibleIds: run.Modifiers.Select(item => item.InstanceId).ToHashSet());
        observation?.ModifierTicks(result.Value.Run ?? run, updatedRun);
        return Result<CombatRelicLifecycleResult>.Success(result.Value with { Run = updatedRun, ResponseSteps = terminalResponses.Steps, ResponseApplications = terminalResponses.Records.ToImmutableArray() });
    }

    public Result<CombatFlowAdvanceResult> AdvanceActivation(RunState run, CombatState combat, DeckState deck, DeterministicContext runDeterminism, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition turnOrderPolicy) => WithOccurrenceObservation(run, combat, cursor => AdvanceActivationCore(run, combat, deck, runDeterminism, sequence, policies, turnOrderPolicy, cursor));

    private Result<CombatFlowAdvanceResult> AdvanceActivationCore(RunState run, CombatState combat, DeckState deck, DeterministicContext runDeterminism, PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies, TurnOrderPolicyDefinition turnOrderPolicy, ObservationCursor? observation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(runDeterminism);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(turnOrderPolicy);
        var traversal = ActivationSequencePolicy.Validate(policies.ActivationSequence);
        if (traversal.IsFailure) return Result<CombatFlowAdvanceResult>.Failure(traversal.Error);
        if (!policies.CommandConditions.IsEmpty) {
            if (_exitGuard == null) return Result<CombatFlowAdvanceResult>.Failure("Configured activation exit requires canonical legality services");
            var gate = _exitGuard.Validate(run with { Deck = deck }, combat);
            if (gate.IsFailure) return Result<CombatFlowAdvanceResult>.Failure(gate.Error);
        }
        return AdvanceWithLifecycle(
            run,
            combat,
            deck,
            runDeterminism,
            sequence,
            policies,
            turnOrderPolicy, observation);
    }

    private Result<CombatFlowAdvanceResult> AdvanceWithLifecycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy, ObservationCursor? observation = null)
    {
        var planned = EndActivation(
            run,
            combat,
            deck,
            runDeterminism,
            sequence,
            policies,
            _phases, observation);
        if (planned.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(planned.Error);

        var steps = new List<CombatResolutionStep> { planned.Value };
        var current = planned.Value.Combat;
        var endedDeck = planned.Value.Deck;
        run = (planned.Value.RunSnapshot ?? run) with
        {
            Deck = endedDeck,
            Determinism = planned.Value.RunDeterminism!.AdvanceStep()
        };
        var endedActorId = combat.ActivationState!.ActiveActorId!;
        var endActivation = AppendBoundary(
            run,
            steps,
            current,
            endedDeck,
            StatusTriggerBoundary.EndActivation,
            endedActorId,
            policies, observation);
        if (endActivation.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(endActivation.Error);
        current = endActivation.Value;
        run = RunAfterBoundary(run, steps);
        endedDeck = run.Deck;
        if (!current.IsActive)
            return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });

        var endResources = AppendResourceBoundary(
            run,
            steps,
            current,
            endedDeck,
            endedActorId,
            RegenerationTiming.END_TURN,
            policies, observation);
        if (endResources.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(endResources.Error);
        current = endResources.Value;
        run = RunAfterBoundary(run, steps);
        endedDeck = run.Deck;
        if (!current.IsActive)
            return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });

        var startsNewRound = ActivationSequenceReducer.StartsNewRound(current, policies.ActivationSequence!);
        if (startsNewRound)
        {
            var endRound = AppendBoundary(
                run,
                steps,
                current,
                endedDeck,
                StatusTriggerBoundary.EndRound,
                endedActorId,
                policies, observation);
            if (endRound.IsFailure)
                return Result<CombatFlowAdvanceResult>.Failure(endRound.Error);
            current = endRound.Value;
            run = RunAfterBoundary(run, steps);
            endedDeck = run.Deck;
            if (!current.IsActive)
                return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });
        }

        var reordered = _turnOrder.CompleteActivation(
            current,
            turnOrderPolicy,
            endedActorId,
            startsNewRound);
        if (reordered.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(reordered.Error);
        current = reordered.Value.State;

        var recalculated = StartActivation(run, current, endedDeck, run.Determinism, policies, observation);
        if (recalculated.IsFailure) return Result<CombatFlowAdvanceResult>.Failure(recalculated.Error);
        var startedStep = recalculated.Value;
        current = startedStep.Combat with
        {
            CurrentTurn = startedStep.Combat.ActivationState!.Round
        };
        run = (startedStep.RunSnapshot ?? run) with
        {
            Deck = startedStep.Deck,
            Determinism = startedStep.RunDeterminism!
        };

        if (startsNewRound)
        {
            var startRound = AppendBoundary(
                run,
                steps,
                current,
                run.Deck,
                StatusTriggerBoundary.StartRound,
                current.ActivationState!.ActiveActorId,
                policies, observation);
            if (startRound.IsFailure)
                return Result<CombatFlowAdvanceResult>.Failure(startRound.Error);
            current = startRound.Value;
            run = RunAfterBoundary(run, steps);
            startedStep = startedStep with { Deck = run.Deck, RunDeterminism = run.Determinism };
            if (!current.IsActive)
                return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });
        }

        steps.Add(startedStep with { Combat = current, RunSnapshot = run with { Deck = startedStep.Deck } });
        run = run with { Deck = startedStep.Deck, Determinism = startedStep.RunDeterminism!.AdvanceStep() };

        var startResources = AppendResourceBoundary(
            run,
            steps,
            current,
            run.Deck,
            current.ActivationState!.ActiveActorId!,
            RegenerationTiming.START_TURN,
            policies, observation);
        if (startResources.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(startResources.Error);
        current = startResources.Value;
        run = RunAfterBoundary(run, steps);
        if (!current.IsActive)
            return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });

        var startActivation = AppendBoundary(
            run,
            steps,
            current,
            run.Deck,
            StatusTriggerBoundary.StartActivation,
            current.ActivationState!.ActiveActorId,
            policies, observation);
        if (startActivation.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(startActivation.Error);
        current = startActivation.Value;
        run = RunAfterBoundary(run, steps);
        if (!current.IsActive)
            return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });

        var enteredPhase = EnterPhase(run, current, sequence, _phases, observation);
        if (enteredPhase.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(enteredPhase.Error);
        steps.Add(enteredPhase.Value);

        return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });
    }

    private Result<CombatState> AppendResourceBoundary(
        RunState run,
        ICollection<CombatResolutionStep> steps,
        CombatState combat,
        DeckState deck,
        string actorId,
        RegenerationTiming timing,
        CombatFlowPoliciesDefinition policies, ObservationCursor? observation = null)
    {
        var processed = ApplyResourceLifecycle(run, combat, actorId, timing, policies, observation);
        if (processed.IsFailure)
            return Result<CombatState>.Failure(processed.Error);
        if (processed.Value.Records.Count == 0)
            return Result<CombatState>.Success(processed.Value.Combat);

        var evaluated = _outcomes.Evaluate(
            processed.Value.Combat,
            policies.Outcome,
            actorId,
            CombatOutcomeEvaluationPoint.LifecycleBoundary);
        steps.Add(new CombatResolutionStep
        {
            TransitionType = $"combat.resources.{ToSnakeCase(timing)}",
            Combat = evaluated,
            Deck = processed.Value.Run?.Deck ?? deck,
            RunSnapshot = processed.Value.Run ?? run,
            RunDeterminism = (processed.Value.Run ?? run).Determinism,
            EffectSteps = processed.Value.Steps,
            Calculations = processed.Value.Steps
                .Where(item => item.Calculation != null)
                .Select(item => item.Calculation!)
                .ToArray(),
            Applications = processed.Value.Records,
            Payload = JsonSerializer.SerializeToElement(new
            {
                timing = timing.ToString(),
                actorId,
                effects = processed.Value.Records,
                steps = processed.Value.Steps,
                processed.Value.Fingerprint
            })
        });
        return Result<CombatState>.Success(evaluated);
    }

    private static RunState RunAfterBoundary(RunState run, IReadOnlyList<CombatResolutionStep> steps)
    {
        var step = steps[^1];
        if (step.RunDeterminism != null && step.RunDeterminism.Step < run.Determinism.Step) return run;
        return (step.RunSnapshot ?? run) with
        { Deck = step.Deck, Determinism = (step.RunDeterminism ?? run.Determinism).AdvanceStep() };
    }

    private Result<CombatResourceLifecycleResult> ApplyResourceLifecycle(
        RunState run,
        CombatState combat,
        string actorId,
        RegenerationTiming timing,
        CombatFlowPoliciesDefinition policies, ObservationCursor? observation = null)
    {
        IReadOnlySet<string>? exclusions = null;
        if (timing == RegenerationTiming.START_TURN &&
            ScopeApplies(policies.ResourceCycle.ActorScope, combat, run, actorId))
        {
            exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                policies.ResourceCycle.ResourceId
            };
        }
        return observation == null ? _resourceLifecycle.Process(run, combat, actorId, timing, exclusions)
            : observation.Resource(_resourceLifecycle, run, combat, actorId, timing, exclusions);
    }

    private static Result<CombatInitializationResult> InitializeActivation(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy,
        ITurnOrderResolver turnOrder, ObservationCursor? observation = null)
    {
        var validation = PhaseSequenceValidator.Validate(sequence);
        if (validation.IsFailure) return Result<CombatInitializationResult>.Failure(validation.Error);

        if (run.Scenario is { } scenario)
            combat = combat with { Relationships = scenario.Relationships, Sides = scenario.Sides };

        var resolvedOrder = turnOrder.Initialize(combat, turnOrderPolicy);
        if (resolvedOrder.IsFailure)
            return Result<CombatInitializationResult>.Failure(resolvedOrder.Error);
        combat = resolvedOrder.Value.State;
        var order = resolvedOrder.Value.Order;
        if (order.Count == 0)
            return Result<CombatInitializationResult>.Failure("No alive actors are available for combat activation");

        var sequenceValidation = ActivationSequencePolicy.Validate(policies.ActivationSequence);
        if (sequenceValidation.IsFailure) return Result<CombatInitializationResult>.Failure(sequenceValidation.Error);
        var circular = policies.ActivationSequence!.Strategy == ActivationSequenceStrategy.Circular;
        if (circular && (turnOrderPolicy.RecalculateAt != TurnOrderRecalculationBoundary.CombatStart ||
            turnOrderPolicy.Strategy == TurnOrderStrategy.Atb))
            return Result<CombatInitializationResult>.Failure("Circular requires a seat order fixed at CombatStart");
        var actorId = circular && policies.ActivationSequence.Circular!.FirstActor == SequenceFirstActor.RunOwner
            ? run.PlayerEntityId : order[0];
        if (!order.Contains(actorId, StringComparer.Ordinal)) return Result<CombatInitializationResult>.Failure("First actor is not eligible");
        var refreshed = RefreshActorResource(combat, actorId, policies.ResourceCycle, run, observation);
        if (refreshed.IsFailure)
            return Result<CombatInitializationResult>.Failure(refreshed.Error);

        var activation = new ActivationState
        {
            ActiveActorId = actorId,
            Round = 1,
            ActivationIndex = order.ToList().IndexOf(actorId),
            Circular = circular ? new CircularSequenceState { Direction = policies.ActivationSequence.Circular!.InitialDirection,
                RoundLength = policies.ActivationSequence.Circular.ActivationsPerRound ?? order.Count } : null,
            ActivationNumber = 1,
            ActionsTaken = 0,
            ActivationOrder = order,
            CompletedActorIds = [],
            WaitingForInput = IsPlayerActor(refreshed.Value.State, actorId),
            RunId = run.RunId,
            StartedAtUtc = refreshed.Value.State.Determinism.LogicalTimestamp.UtcDateTime
        };

        return Result<CombatInitializationResult>.Success(CreateInitializationResult(refreshed.Value.State with
        {
            ActivationState = activation
        }, refreshed.Value.Run ?? run, refreshed.Value.Steps, refreshed.Value.Records));
    }

    private Result<CombatResolutionStep> EndActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        IPhaseGraphReducer phases, ObservationCursor? observation = null)
    {
        var activation = combat.ActivationState;
        if (activation == null || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return Result<CombatResolutionStep>.Failure("Combat activation has not been initialized");
        if (!combat.IsActive)
            return Result<CombatResolutionStep>.Failure("A terminal combat cannot advance activation");

        run = run with { Deck = deck, Determinism = runDeterminism };
        var exited = observation == null ? phases.Exit(run, combat, sequence) : observation.Phase(phases, run, combat, sequence, false);
        if (exited.IsFailure)
            return Result<CombatResolutionStep>.Failure(exited.Error);
        run = exited.Value.Run;
        activation = exited.Value.Combat.ActivationState ?? activation;
        if (string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return Result<CombatResolutionStep>.Failure("Phase exit invalidated the activation actor");
        var endedDeck = ApplyEndCardZoneLifecycle(
            run,
            run.Deck,
            activation.ActiveActorId,
            run.Determinism, exited.Value.Combat, observation);
        if (endedDeck.IsFailure)
            return Result<CombatResolutionStep>.Failure(endedDeck.Error);

        var completed = activation.CompletedActorIds
            .Append(activation.ActiveActorId)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var endedCombat = exited.Value.Combat with
        {
            ActivationState = activation with
            {
                CompletedActorIds = completed,
                WaitingForInput = false
            }
        };
        var endStep = new CombatResolutionStep
        {
            TransitionType = "combat.activation.ended",
            Combat = endedCombat,
            Deck = endedDeck.Value.Deck,
            RunDeterminism = endedDeck.Value.Context,
            RunSnapshot = run with { Deck = endedDeck.Value.Deck, Determinism = endedDeck.Value.Context },
            EffectSteps = exited.Value.Steps,
            Calculations = exited.Value.Calculations,
            Applications = exited.Value.Applications,
            CardZoneSteps = endedDeck.Value.CardZoneSteps,
            Payload = JsonSerializer.SerializeToElement(new
            {
                actorId = activation.ActiveActorId,
                activation.Round,
                activation.ActivationNumber,
                phaseTransitions = exited.Value.Transitions,
                affectedCardInstanceIds = endedDeck.Value.AffectedInstanceIds
            })
        };

        return Result<CombatResolutionStep>.Success(endStep);
    }

    private Result<CombatResolutionStep> StartActivation(
        RunState run, CombatState endedCombat, DeckState deck, DeterministicContext drawContext,
        CombatFlowPoliciesDefinition policies, ObservationCursor? observation = null)
    {
        var activation = endedCombat.ActivationState!;
        var successor = ActivationSequenceReducer.Next(endedCombat, policies.ActivationSequence!);
        if (successor.IsFailure) return Result<CombatResolutionStep>.Failure(successor.Error);
        var (nextActorId, nextIndex, round, nextOrder, nextCompleted, circular) = successor.Value;
        var refreshed = RefreshActorResource(endedCombat, nextActorId, policies.ResourceCycle, run, observation);
        if (refreshed.IsFailure)
            return Result<CombatResolutionStep>.Failure(refreshed.Error);

        run = refreshed.Value.Run ?? run;
        var nextActivation = activation with
        {
            ActiveActorId = nextActorId,
            Round = round,
            ActivationIndex = nextIndex,
            ActivationNumber = checked(activation.ActivationNumber + 1),
            ActionsTaken = 0,
            ActivationOrder = nextOrder,
            CompletedActorIds = nextCompleted,
            Circular = circular,
            Facts = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal),
            WaitingForInput = IsPlayerActor(refreshed.Value.State, nextActorId),
            StartedAtUtc = refreshed.Value.State.Determinism.LogicalTimestamp.UtcDateTime
        };
        var startedCombat = refreshed.Value.State with
        {
            ActivationState = nextActivation
        };
        var startedDeck = ApplyStartCardZoneLifecycle(
            run,
            deck,
            nextActorId,
            drawContext, startedCombat, observation);
        if (startedDeck.IsFailure)
            return Result<CombatResolutionStep>.Failure(startedDeck.Error);

        run = run with { Deck = startedDeck.Value.Deck, Determinism = startedDeck.Value.Context };
        var startStep = new CombatResolutionStep
        {
            TransitionType = "combat.activation.started",
            Combat = startedCombat,
            Deck = run.Deck,
            RunDeterminism = run.Determinism,
            RunSnapshot = run,
            CardZoneSteps = startedDeck.Value.CardZoneSteps,
            EffectSteps = refreshed.Value.Steps, Calculations = refreshed.Value.Calculations, Applications = refreshed.Value.Records,
            Payload = JsonSerializer.SerializeToElement(new
            {
                actorId = nextActorId,
                round,
                activationNumber = nextActivation.ActivationNumber,
                newlyPlayableCardDefinitionIds = startedDeck.Value.NewlyPlayableDefinitionIds
            })
        };

        return Result<CombatResolutionStep>.Success(startStep);
    }

    private static Result<CombatResolutionStep> EnterPhase(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        IPhaseGraphReducer phases, ObservationCursor? observation = null)
    {
        var entered = observation == null ? phases.Enter(run, combat, sequence) : observation.Phase(phases, run, combat, sequence, true);
        if (entered.IsFailure) return Result<CombatResolutionStep>.Failure(entered.Error);
        return Result<CombatResolutionStep>.Success(new CombatResolutionStep
        {
            TransitionType = "combat.phase.entered",
            Combat = entered.Value.Combat,
            Deck = entered.Value.Run.Deck,
            RunDeterminism = entered.Value.Run.Determinism,
            RunSnapshot = entered.Value.Run,
            EffectSteps = entered.Value.Steps,
            Calculations = entered.Value.Calculations,
            Applications = entered.Value.Applications,
            Payload = JsonSerializer.SerializeToElement(new
            {
                sequenceId = sequence.SequenceId,
                cursor = entered.Value.Combat.PhaseState?.Cursor,
                phaseTransitions = entered.Value.Transitions,
                entered.Value.Fingerprint
            })
        });
    }

    private Result<CombatInitializationResult> ApplyInitialLifecycle(
        RunState run,
        CombatState combat,
        CombatFlowPoliciesDefinition policies, ObservationCursor? observation = null)
    {
        var current = combat;
        var effectSteps = new List<EffectExecutionStep>();
        var applications = new List<EffectApplicationRecord>();
        foreach (var boundary in new[]
                 {
                     StatusTriggerBoundary.StartRound,
                     StatusTriggerBoundary.StartActivation
                 })
        {
            if (boundary == StatusTriggerBoundary.StartRound && policies.ActivationSequence?.Strategy == ActivationSequenceStrategy.Circular &&
                policies.ActivationSequence.Circular?.RoundPolicy == SequenceRoundStrategy.Disabled) continue;
            using var boundaryFrame = observation?.Boundary(ToGameplayBoundary(boundary), boundary.ToString(), current);
            var responses = BoundaryResponses(observation, run, current);
            run = responses.Run ?? run; current = responses.State;
            effectSteps.AddRange(responses.Steps); applications.AddRange(responses.Records);
            var processed = !policies.StatusTiming.Boundaries.Contains(boundary)
                ? Result<CombatStatusLifecycleResult>.Success(new(current, [])) : (observation == null ? _statusLifecycle.Process(run, current, boundary, current.ActivationState?.ActiveActorId)
                    : observation.Status(_statusLifecycle, run, current, boundary, current.ActivationState?.ActiveActorId));
            if (processed.IsFailure)
                return Result<CombatInitializationResult>.Failure(processed.Error);
            run = processed.Value.Run ?? run;
            var relics = (observation == null ? _relicLifecycle.Process(run, processed.Value.Combat, boundary.ToString())
                : observation.Relic(_relicLifecycle, run, processed.Value.Combat, boundary.ToString()));
            if (relics.IsFailure) return Result<CombatInitializationResult>.Failure(relics.Error);
            run = relics.Value.Run ?? run;
            effectSteps.AddRange(processed.Value.Events.SelectMany(item => item.Steps).Concat(processed.Value.ResponseSteps));
            effectSteps.AddRange(relics.Value.Events.SelectMany(item => item.Steps));
            applications.AddRange(processed.Value.Events.SelectMany(item => item.Applications).Concat(processed.Value.ResponseApplications));
            applications.AddRange(relics.Value.Events.SelectMany(item => item.Applications));
            current = _outcomes.Evaluate(
                relics.Value.Combat,
                policies.Outcome,
                current.ActivationState?.ActiveActorId,
                CombatOutcomeEvaluationPoint.LifecycleBoundary);
            if (!current.IsActive)
                break;
        }
        return Result<CombatInitializationResult>.Success(CreateInitializationResult(
            current,
            run,
            effectSteps,
            applications));
    }

    private static CombatInitializationResult CreateInitializationResult(
        CombatState combat,
        RunState run,
        IEnumerable<EffectExecutionStep> effectSteps,
        IEnumerable<EffectApplicationRecord> applications,
        IEnumerable<PhaseTransitionRecord>? phaseTransitions = null,
        IEnumerable<CardZoneFlowStepRecord>? cardZoneSteps = null)
    {
        var normalizedSteps = effectSteps
            .Select((step, index) => step with { Index = index })
            .ToImmutableArray();
        var immutableApplications = applications.ToImmutableArray();
        var immutablePhaseTransitions = phaseTransitions?.ToImmutableArray() ?? [];
        var immutableCardZoneSteps = cardZoneSteps?.ToImmutableArray() ?? [];
        var calculations = normalizedSteps
            .Where(step => step.Calculation != null)
            .Select(step => step.Calculation!)
            .ToImmutableArray();
        return new CombatInitializationResult(combat, run)
        {
            EffectSteps = normalizedSteps,
            Calculations = calculations,
            Applications = immutableApplications,
            PhaseTransitions = immutablePhaseTransitions,
            CardZoneSteps = immutableCardZoneSteps,
            Fingerprint = CanonicalJson.ComputeObjectHash(
                CanonicalJson.Property("combat", combat), CanonicalJson.Property("run", run),
                CanonicalJson.Property("effectSteps", normalizedSteps),
                CanonicalJson.Property("applications", immutableApplications),
                CanonicalJson.Property("phaseTransitions", immutablePhaseTransitions),
                CanonicalJson.Property("cardZoneSteps", immutableCardZoneSteps))
        };
    }

    private Result<CombatState> AppendBoundary(
        RunState run,
        ICollection<CombatResolutionStep> steps,
        CombatState combat,
        DeckState deck,
        StatusTriggerBoundary boundary,
        string? activeActorId,
        CombatFlowPoliciesDefinition policies, ObservationCursor? observation = null)
    {
        using var boundaryFrame = observation?.Boundary(ToGameplayBoundary(boundary), boundary.ToString(), combat);
        run = run with { Deck = deck };
        var responses = BoundaryResponses(observation, run, combat);
        run = responses.Run ?? run; combat = responses.State;
        var eligibleModifiers = run.Modifiers.Select(item => item.InstanceId).ToHashSet();
        var processed = !policies.StatusTiming.Boundaries.Contains(boundary)
            ? Result<CombatStatusLifecycleResult>.Success(new(combat, []))
            : (observation == null ? _statusLifecycle.Process(run, combat, boundary, activeActorId)
                : observation.Status(_statusLifecycle, run, combat, boundary, activeActorId));
        if (processed.IsFailure)
            return Result<CombatState>.Failure(processed.Error);
        run = processed.Value.Run ?? run;
        var relics = (observation == null ? _relicLifecycle.Process(run, processed.Value.Combat, boundary.ToString())
                : observation.Relic(_relicLifecycle, run, processed.Value.Combat, boundary.ToString()));
        if (relics.IsFailure) return Result<CombatState>.Failure(relics.Error);
        run = relics.Value.Run ?? run;
        if (boundary is StatusTriggerBoundary.EndActivation or StatusTriggerBoundary.EndRound)
        {
            var beforeTick = run;
            run = Core.Combat.Modifiers.ModifierTransitions.Tick(run,
                boundary == StatusTriggerBoundary.EndActivation ? Core.Combat.Modifiers.ModifierDurationBoundary.Activation
                    : Core.Combat.Modifiers.ModifierDurationBoundary.Round, combat, activeActorId, eligibleModifiers);
            observation?.ModifierTicks(beforeTick, run);
        }
        var evaluated = _outcomes.Evaluate(
            relics.Value.Combat,
            policies.Outcome,
            activeActorId,
            CombatOutcomeEvaluationPoint.LifecycleBoundary);
        var effectSteps = responses.Steps.Concat(processed.Value.Events.SelectMany(item => item.Steps)).Concat(processed.Value.ResponseSteps)
            .Concat(relics.Value.Events.SelectMany(item => item.Steps))
            .ToArray();
        steps.Add(new CombatResolutionStep
        {
            TransitionType = $"combat.status.{ToSnakeCase(boundary)}",
            Combat = evaluated,
            Deck = run.Deck,
            RunSnapshot = run,
            RunDeterminism = run.Determinism,
            EffectSteps = effectSteps,
            Calculations = effectSteps
                .Where(item => item.Calculation != null)
                .Select(item => item.Calculation!)
                .ToArray(),
            Applications = responses.Records.Concat(processed.Value.Events.SelectMany(item => item.Applications)).Concat(processed.Value.ResponseApplications)
                .Concat(relics.Value.Events.SelectMany(item => item.Applications))
                .ToArray(),
            Payload = JsonSerializer.SerializeToElement(new
            {
                boundary = boundary.ToString(),
                activeActorId,
                events = processed.Value.Events,
                relicEvents = relics.Value.Events
            })
        });
        return Result<CombatState>.Success(evaluated);
    }

    private static string ToSnakeCase(StatusTriggerBoundary boundary) => boundary switch
    {
        StatusTriggerBoundary.StartActivation => "start_activation",
        StatusTriggerBoundary.EndActivation => "end_activation",
        StatusTriggerBoundary.StartRound => "start_round",
        StatusTriggerBoundary.EndRound => "end_round",
        _ => "unknown"
    };

    private static Core.GameplayEvents.GameplayBoundary ToGameplayBoundary(StatusTriggerBoundary boundary) => boundary switch
    {
        StatusTriggerBoundary.StartActivation => Core.GameplayEvents.GameplayBoundary.ActivationStarted,
        StatusTriggerBoundary.EndActivation => Core.GameplayEvents.GameplayBoundary.ActivationEnded,
        StatusTriggerBoundary.StartRound => Core.GameplayEvents.GameplayBoundary.RoundStarted,
        StatusTriggerBoundary.EndRound => Core.GameplayEvents.GameplayBoundary.RoundEnded,
        _ => Core.GameplayEvents.GameplayBoundary.Unspecified
    };

    private static string ToSnakeCase(RegenerationTiming timing) => timing switch
    {
        RegenerationTiming.START_TURN => "start_activation",
        RegenerationTiming.END_TURN => "end_activation",
        RegenerationTiming.OUT_OF_COMBAT => "out_of_combat",
        _ => "unknown"
    };

    private Result<CardZoneActivationStartResult> ApplyStartCardZoneLifecycle(
        RunState run,
        DeckState deck,
        string actorId,
        DeterministicContext context, CombatState? combatSnapshot = null, ObservationCursor? observation = null)
    {
        var result = CardZoneCombatLifecycle.Start(_cardZoneFlows, run, deck, context, actorId, combatSnapshot);
        if (result.IsSuccess) observation?.Cards(result.Value.CardZoneSteps, run with { Deck = deck }, run with { Deck = result.Value.Deck }, actorId);
        return result;
    }

    private Result<CardZoneActivationEndResult> ApplyEndCardZoneLifecycle(
        RunState run,
        DeckState deck,
        string actorId,
        DeterministicContext context, CombatState? combatSnapshot = null, ObservationCursor? observation = null)
    {
        var result = CardZoneCombatLifecycle.End(_cardZoneFlows, run, deck, context, actorId, combatSnapshot);
        if (result.IsSuccess) observation?.Cards(result.Value.CardZoneSteps, run with { Deck = deck }, run with { Deck = result.Value.Deck }, actorId);
        return result;
    }

    private static bool ScopeApplies(
        FlowActorScope scope,
        CombatState combat,
        RunState run,
        string actorId)
    {
        var actor = combat.GetActor(actorId);
        if (actor == null)
            return false;
        return scope switch
        {
            FlowActorScope.RunOwner => string.Equals(actorId, run.PlayerEntityId, StringComparison.Ordinal),
            FlowActorScope.PlayerControlled => combat.ControllerOf(actor) == ControllerKind.Player,
            FlowActorScope.AiControlled => combat.ControllerOf(actor) == ControllerKind.AI,
            FlowActorScope.All => true,
            _ => false
        };
    }

    private static bool IsPlayerActor(CombatState combat, string actorId) =>
        combat.GetActor(actorId) is { } actor &&
        combat.ControllerOf(actor) == ControllerKind.Player;

}
