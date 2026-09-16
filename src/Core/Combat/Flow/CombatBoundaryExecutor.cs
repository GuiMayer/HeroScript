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
}

/// <summary>
/// Orders configured combat lifecycle boundaries and produces immutable frames.
/// It owns no persistence, automatic-controller loop or runtime publication.
/// </summary>
public sealed class CombatBoundaryExecutor : ICombatBoundaryExecutor
{
    private readonly ITurnOrderResolver _turnOrder;
    private readonly IRunCardResolver _cards;
    private readonly ICombatStatusLifecycle _statusLifecycle;
    private readonly ICombatRelicLifecycle _relicLifecycle;
    private readonly ICombatResourceLifecycle _resourceLifecycle;
    private readonly IPhaseGraphReducer _phases;
    private readonly ICombatOutcomeResolver _outcomes;
    private readonly ICardZoneFlowExecutor? _cardZoneFlows;

    public CombatBoundaryExecutor(
        ITurnOrderResolver turnOrder,
        IRunCardResolver cards,
        ICombatStatusLifecycle statusLifecycle,
        ICombatRelicLifecycle relicLifecycle,
        ICombatResourceLifecycle resourceLifecycle,
        IPhaseGraphReducer phases,
        ICombatOutcomeResolver outcomes,
        ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        _turnOrder = turnOrder ?? throw new ArgumentNullException(nameof(turnOrder));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _statusLifecycle = statusLifecycle ?? throw new ArgumentNullException(nameof(statusLifecycle));
        _relicLifecycle = relicLifecycle ?? throw new ArgumentNullException(nameof(relicLifecycle));
        _resourceLifecycle = resourceLifecycle ?? throw new ArgumentNullException(nameof(resourceLifecycle));
        _phases = phases ?? throw new ArgumentNullException(nameof(phases));
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
        _cardZoneFlows = cardZoneFlows;
    }

    public Result<CombatInitializationResult> InitializeTransaction(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(turnOrderPolicy);
        var effectSteps = new List<EffectExecutionStep>();
        var applications = new List<EffectApplicationRecord>();
        var phaseTransitions = new List<PhaseTransitionRecord>();
        var cardZoneSteps = new List<CardZoneFlowStepRecord>();
        if (run.ResolvedMode?.CardZoneSystem != null)
        {
            var deficit = System.Math.Max(0,
                policies.DeckCycle.InitialHandSize - run.Deck.HandInstanceIds.Count);
            var flowed = CardZoneRunFlowDispatcher.Execute(_cardZoneFlows, run, run.Deck,
                run.Determinism, "encounter.started",
                variables: new Dictionary<string, double> { ["initialHandDeficit"] = deficit });
            if (flowed.IsFailure) return Result<CombatInitializationResult>.Failure(flowed.Error);
            cardZoneSteps.AddRange(flowed.Value.Steps);
            run = run with
            {
                Deck = new DeckState { Topology = flowed.Value.State },
                Determinism = flowed.Value.Context
            };
        }
        else
        {
            var deck = DeckTransitions.BeginEncounter(run.Deck, policies.DeckCycle, run.Determinism);
            if (deck.IsFailure) return Result<CombatInitializationResult>.Failure(deck.Error);
            run = run with { Deck = deck.Value.State, Determinism = deck.Value.Context };
        }
        var initialized = InitializeActivation(
            run,
            combat,
            sequence,
            policies,
            turnOrderPolicy,
            _turnOrder);
        if (initialized.IsFailure) return Result<CombatInitializationResult>.Failure(initialized.Error);
        var regenerated = ApplyResourceLifecycle(
            run,
            initialized.Value,
            initialized.Value.ActivationState!.ActiveActorId!,
            RegenerationTiming.START_TURN,
            policies);
        if (regenerated.IsFailure) return Result<CombatInitializationResult>.Failure(regenerated.Error);
        run = regenerated.Value.Run ?? run;
        effectSteps.AddRange(regenerated.Value.Steps);
        applications.AddRange(regenerated.Value.Records);
        var relics = _relicLifecycle.Process(run, regenerated.Value.Combat, CombatTriggerBoundaries.CombatStart);
        if (relics.IsFailure) return Result<CombatInitializationResult>.Failure(relics.Error);
        run = relics.Value.Run ?? run;
        effectSteps.AddRange(relics.Value.Events.SelectMany(item => item.Steps));
        applications.AddRange(relics.Value.Events.SelectMany(item => item.Applications));
        var afterRelics = _outcomes.Evaluate(
            relics.Value.Combat,
            policies.Outcome,
            initialized.Value.ActivationState?.ActiveActorId,
            CombatOutcomeEvaluationPoint.LifecycleBoundary);
        var initial = ApplyInitialLifecycle(run, afterRelics, policies);
        if (initial.IsFailure) return initial;
        run = initial.Value.Run;
        effectSteps.AddRange(initial.Value.EffectSteps);
        applications.AddRange(initial.Value.Applications);
        var current = initial.Value.Combat;
        if (current.IsActive)
        {
            var entered = _phases.Enter(run, current, sequence);
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
            var completed = Complete(run, current);
            if (completed.IsFailure) return Result<CombatInitializationResult>.Failure(completed.Error);
            effectSteps.AddRange(completed.Value.Events.SelectMany(item => item.Steps));
            applications.AddRange(completed.Value.Events.SelectMany(item => item.Applications));
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

    public Result<CombatRelicLifecycleResult> Complete(RunState run, CombatState combat)
    {
        if (combat.IsActive) return Result<CombatRelicLifecycleResult>.Failure("Cannot finalize an active combat");
        var result = _relicLifecycle.Process(run, combat, CombatTriggerBoundaries.CombatEnd);
        if (result.IsFailure || combat.CompletedLifecycleBoundaries.Contains(CombatTriggerBoundaries.CombatEnd)) return result;
        var updatedRun = Core.Combat.Modifiers.ModifierTransitions.Tick(result.Value.Run ?? run,
            Core.Combat.Modifiers.ModifierDurationBoundary.Combat, eligibleIds: run.Modifiers.Select(item => item.InstanceId).ToHashSet());
        return Result<CombatRelicLifecycleResult>.Success(result.Value with { Run = updatedRun });
    }

    public Result<CombatFlowAdvanceResult> AdvanceActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(runDeterminism);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(turnOrderPolicy);
        return AdvanceWithLifecycle(
            run,
            combat,
            deck,
            runDeterminism,
            sequence,
            policies,
            turnOrderPolicy);
    }

    private Result<CombatFlowAdvanceResult> AdvanceWithLifecycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy)
    {
        var planned = EndActivation(
            run,
            combat,
            deck,
            runDeterminism,
            sequence,
            policies,
            instance => ResolveCardTags(run, instance),
            _phases);
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
            policies);
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
            policies);
        if (endResources.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(endResources.Error);
        current = endResources.Value;
        run = RunAfterBoundary(run, steps);
        endedDeck = run.Deck;
        if (!current.IsActive)
            return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });

        var startsNewRound = StartsNewRound(current);
        if (startsNewRound)
        {
            var endRound = AppendBoundary(
                run,
                steps,
                current,
                endedDeck,
                StatusTriggerBoundary.EndRound,
                endedActorId,
                policies);
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

        var recalculated = StartActivation(run, current, endedDeck, run.Determinism, policies);
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
                policies);
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
            policies);
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
            policies);
        if (startActivation.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(startActivation.Error);
        current = startActivation.Value;
        run = RunAfterBoundary(run, steps);
        if (!current.IsActive)
            return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });

        var enteredPhase = EnterPhase(run, current, sequence, _phases);
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
        CombatFlowPoliciesDefinition policies)
    {
        var processed = ApplyResourceLifecycle(run, combat, actorId, timing, policies);
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
        CombatFlowPoliciesDefinition policies)
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
        return _resourceLifecycle.Process(run, combat, actorId, timing, exclusions);
    }

    private static Result<CombatState> InitializeActivation(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        TurnOrderPolicyDefinition turnOrderPolicy,
        ITurnOrderResolver turnOrder)
    {
        var validation = PhaseSequenceValidator.Validate(sequence);
        if (validation.IsFailure) return Result<CombatState>.Failure(validation.Error);

        if (run.Scenario is { } scenario)
            combat = combat with { Relationships = scenario.Relationships, Sides = scenario.Sides };

        var resolvedOrder = turnOrder.Initialize(combat, turnOrderPolicy);
        if (resolvedOrder.IsFailure)
            return Result<CombatState>.Failure(resolvedOrder.Error);
        combat = resolvedOrder.Value.State;
        var order = resolvedOrder.Value.Order;
        if (order.Count == 0)
            return Result<CombatState>.Failure("No alive actors are available for combat activation");

        var actorId = order[0];
        var refreshed = RefreshActorResource(combat, actorId, policies.ResourceCycle, run);
        if (refreshed.IsFailure)
            return Result<CombatState>.Failure(refreshed.Error);

        var activation = new ActivationState
        {
            ActiveActorId = actorId,
            Round = 1,
            ActivationIndex = 0,
            ActivationNumber = 1,
            ActionsTaken = 0,
            ActivationOrder = order,
            CompletedActorIds = [],
            WaitingForInput = IsPlayerActor(refreshed.Value, actorId),
            RunId = run.RunId,
            StartedAtUtc = refreshed.Value.Determinism.LogicalTimestamp.UtcDateTime
        };

        return Result<CombatState>.Success(refreshed.Value with
        {
            ActivationState = activation
        });
    }

    private Result<CombatResolutionStep> EndActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        Func<CardInstanceState, Result<IReadOnlyList<string>>> resolveCardTags,
        IPhaseGraphReducer phases)
    {
        var activation = combat.ActivationState;
        if (activation == null || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return Result<CombatResolutionStep>.Failure("Combat activation has not been initialized");
        if (!combat.IsActive)
            return Result<CombatResolutionStep>.Failure("A terminal combat cannot advance activation");

        run = run with { Deck = deck, Determinism = runDeterminism };
        var exited = phases.Exit(run, combat, sequence);
        if (exited.IsFailure)
            return Result<CombatResolutionStep>.Failure(exited.Error);
        run = exited.Value.Run;
        var endedDeck = ApplyEndDeckCycle(
            run,
            exited.Value.Combat,
            deck,
            activation.ActiveActorId,
            policies.DeckCycle,
            run.Determinism,
            resolveCardTags);
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
            Deck = endedDeck.Value.State,
            RunDeterminism = endedDeck.Value.Context,
            RunSnapshot = run with { Deck = endedDeck.Value.State, Determinism = endedDeck.Value.Context },
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
                discardedCardIds = endedDeck.Value.Discarded,
                exhaustedCardIds = endedDeck.Value.Exhausted
            })
        };

        return Result<CombatResolutionStep>.Success(endStep);
    }

    private Result<CombatResolutionStep> StartActivation(
        RunState run, CombatState endedCombat, DeckState deck, DeterministicContext drawContext,
        CombatFlowPoliciesDefinition policies)
    {
        var activation = endedCombat.ActivationState!;
        var completed = activation.CompletedActorIds;
        var combat = endedCombat;
        var eligibleOrder = endedCombat.TurnOrderState.Order
            .Where(actorId => combat.GetActor(actorId)?.IsAlive == true)
            .ToArray();
        var nextIndex = Array.FindIndex(
            eligibleOrder,
            actorId => !completed.Contains(actorId, StringComparer.Ordinal));
        var round = activation.Round;
        IReadOnlyList<string> nextOrder = eligibleOrder;
        IReadOnlyList<string> nextCompleted = completed;
        if (nextIndex < 0)
        {
            round = checked(round + 1);
            nextOrder = eligibleOrder;
            nextCompleted = [];
            nextIndex = 0;
        }
        if (nextOrder.Count == 0)
            return Result<CombatResolutionStep>.Failure("No alive actors are available for the next activation");

        var nextActorId = nextOrder[nextIndex];
        var refreshed = RefreshActorResource(endedCombat, nextActorId, policies.ResourceCycle, run);
        if (refreshed.IsFailure)
            return Result<CombatResolutionStep>.Failure(refreshed.Error);

        var startedDeck = ApplyStartDeckCycle(
            run,
            refreshed.Value,
            deck,
            nextActorId,
            policies.DeckCycle,
            drawContext);
        if (startedDeck.IsFailure)
            return Result<CombatResolutionStep>.Failure(startedDeck.Error);

        var nextActivation = activation with
        {
            ActiveActorId = nextActorId,
            Round = round,
            ActivationIndex = nextIndex,
            ActivationNumber = checked(activation.ActivationNumber + 1),
            ActionsTaken = 0,
            ActivationOrder = nextOrder,
            CompletedActorIds = nextCompleted,
            WaitingForInput = IsPlayerActor(refreshed.Value, nextActorId),
            StartedAtUtc = refreshed.Value.Determinism.LogicalTimestamp.UtcDateTime
        };
        var startedCombat = refreshed.Value with
        {
            ActivationState = nextActivation
        };
        run = run with { Deck = startedDeck.Value.State, Determinism = startedDeck.Value.Context };
        var startStep = new CombatResolutionStep
        {
            TransitionType = "combat.activation.started",
            Combat = startedCombat,
            Deck = run.Deck,
            RunDeterminism = run.Determinism,
            RunSnapshot = run,
            CardZoneSteps = startedDeck.Value.CardZoneSteps,
            Payload = JsonSerializer.SerializeToElement(new
            {
                actorId = nextActorId,
                round,
                activationNumber = nextActivation.ActivationNumber,
                drawnCardIds = startedDeck.Value.Cards
            })
        };

        return Result<CombatResolutionStep>.Success(startStep);
    }

    private static Result<CombatResolutionStep> EnterPhase(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        IPhaseGraphReducer phases)
    {
        var entered = phases.Enter(run, combat, sequence);
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
        CombatFlowPoliciesDefinition policies)
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
            var processed = !policies.StatusTiming.Boundaries.Contains(boundary)
                ? Result<CombatStatusLifecycleResult>.Success(new(current, [])) : _statusLifecycle.Process(
                run,
                current,
                boundary,
                current.ActivationState?.ActiveActorId);
            if (processed.IsFailure)
                return Result<CombatInitializationResult>.Failure(processed.Error);
            run = processed.Value.Run ?? run;
            var relics = _relicLifecycle.Process(run, processed.Value.Combat, boundary.ToString());
            if (relics.IsFailure) return Result<CombatInitializationResult>.Failure(relics.Error);
            run = relics.Value.Run ?? run;
            effectSteps.AddRange(processed.Value.Events.SelectMany(item => item.Steps));
            effectSteps.AddRange(relics.Value.Events.SelectMany(item => item.Steps));
            applications.AddRange(processed.Value.Events.SelectMany(item => item.Applications));
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
            Fingerprint = CanonicalJson.ComputeHash(new
            {
                combat,
                run,
                effectSteps = normalizedSteps,
                applications = immutableApplications,
                phaseTransitions = immutablePhaseTransitions,
                cardZoneSteps = immutableCardZoneSteps
            })
        };
    }

    private Result<CombatState> AppendBoundary(
        RunState run,
        ICollection<CombatResolutionStep> steps,
        CombatState combat,
        DeckState deck,
        StatusTriggerBoundary boundary,
        string? activeActorId,
        CombatFlowPoliciesDefinition policies)
    {
        run = run with { Deck = deck };
        var eligibleModifiers = run.Modifiers.Select(item => item.InstanceId).ToHashSet();
        var processed = !policies.StatusTiming.Boundaries.Contains(boundary)
            ? Result<CombatStatusLifecycleResult>.Success(new(combat, []))
            : _statusLifecycle.Process(run, combat, boundary, activeActorId);
        if (processed.IsFailure)
            return Result<CombatState>.Failure(processed.Error);
        run = processed.Value.Run ?? run;
        var relics = _relicLifecycle.Process(run, processed.Value.Combat, boundary.ToString());
        if (relics.IsFailure) return Result<CombatState>.Failure(relics.Error);
        run = relics.Value.Run ?? run;
        if (boundary is StatusTriggerBoundary.EndActivation or StatusTriggerBoundary.EndRound)
            run = Core.Combat.Modifiers.ModifierTransitions.Tick(run,
                boundary == StatusTriggerBoundary.EndActivation ? Core.Combat.Modifiers.ModifierDurationBoundary.Activation
                    : Core.Combat.Modifiers.ModifierDurationBoundary.Round, combat, activeActorId, eligibleModifiers);
        var evaluated = _outcomes.Evaluate(
            relics.Value.Combat,
            policies.Outcome,
            activeActorId,
            CombatOutcomeEvaluationPoint.LifecycleBoundary);
        var effectSteps = processed.Value.Events.SelectMany(item => item.Steps)
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
            Applications = processed.Value.Events.SelectMany(item => item.Applications)
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

    private static bool StartsNewRound(CombatState combat)
    {
        var activation = combat.ActivationState;
        return activation != null && activation.ActivationOrder
            .Where(actorId => combat.GetActor(actorId)?.IsAlive == true)
            .All(actorId => activation.CompletedActorIds.Contains(actorId, StringComparer.Ordinal));
    }

    private static string ToSnakeCase(StatusTriggerBoundary boundary) => boundary switch
    {
        StatusTriggerBoundary.StartActivation => "start_activation",
        StatusTriggerBoundary.EndActivation => "end_activation",
        StatusTriggerBoundary.StartRound => "start_round",
        StatusTriggerBoundary.EndRound => "end_round",
        _ => "unknown"
    };

    private static string ToSnakeCase(RegenerationTiming timing) => timing switch
    {
        RegenerationTiming.START_TURN => "start_activation",
        RegenerationTiming.END_TURN => "end_activation",
        RegenerationTiming.OUT_OF_COMBAT => "out_of_combat",
        _ => "unknown"
    };

    private Result<IReadOnlyList<string>> ResolveCardTags(
        RunState run,
        CardInstanceState instance)
    {
        var effective = _cards.Resolve(run, instance);
        return effective.IsFailure
            ? Result<IReadOnlyList<string>>.Failure(effective.Error)
            : Result<IReadOnlyList<string>>.Success(effective.Value.Tags);
    }

    private Result<ResourceRefreshResult> ApplyStartDeckCycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        string actorId,
        DeckCyclePolicyDefinition policy,
        DeterministicContext context)
    {
        if (run.ResolvedMode?.CardZoneSystem != null)
        {
            var flowed = CardZoneCombatLifecycle.Start(_cardZoneFlows, run,
                deck, context, actorId);
            if (flowed.IsFailure) return Result<ResourceRefreshResult>.Failure(flowed.Error);
            return Result<ResourceRefreshResult>.Success(new ResourceRefreshResult(
                flowed.Value.Deck, flowed.Value.Context, flowed.Value.DrawnCards)
            {
                CardZoneSteps = flowed.Value.CardZoneSteps
            });
        }

        if (!ScopeApplies(policy.ActorScope, combat, run, actorId) || policy.DrawPerActivation == 0)
            return Result<ResourceRefreshResult>.Success(new ResourceRefreshResult(deck, context, []));

        var availableHandSlots = System.Math.Max(0, policy.HandLimit - deck.Hand.Count);
        var count = System.Math.Min(policy.DrawPerActivation, availableHandSlots);
        var drawn = DeckTransitions.Draw(
            deck,
            count,
            context,
            policy.ShuffleDiscardWhenDrawEmpty,
            policy.AllowPartialDraw);
        return drawn.IsFailure
            ? Result<ResourceRefreshResult>.Failure(drawn.Error)
            : Result<ResourceRefreshResult>.Success(
                new ResourceRefreshResult(drawn.Value.State, drawn.Value.Context, drawn.Value.Cards));
    }

    private Result<EndDeckCycleResult> ApplyEndDeckCycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        string actorId,
        DeckCyclePolicyDefinition policy,
        DeterministicContext context,
        Func<CardInstanceState, Result<IReadOnlyList<string>>> resolveCardTags)
    {
        if (run.ResolvedMode?.CardZoneSystem != null)
        {
            var flowed = CardZoneCombatLifecycle.End(_cardZoneFlows, run,
                deck, context, actorId);
            if (flowed.IsFailure) return Result<EndDeckCycleResult>.Failure(flowed.Error);
            return Result<EndDeckCycleResult>.Success(new EndDeckCycleResult(
                flowed.Value.Deck, flowed.Value.Context,
                flowed.Value.DiscardedInstanceIds, flowed.Value.ExhaustedInstanceIds)
            {
                CardZoneSteps = flowed.Value.CardZoneSteps
            });
        }

        if (!ScopeApplies(policy.ActorScope, combat, run, actorId) || deck.Hand.Count == 0)
            return Result<EndDeckCycleResult>.Success(new EndDeckCycleResult(deck, context, [], []));

        var exhaust = new List<string>();
        var discard = new List<string>();
        for (var index = 0; index < deck.Hand.Count; index++)
        {
            var cardInstanceId = deck.HandInstanceIds[index];
            var instance = deck.GetCard(cardInstanceId);
            if (instance == null)
                return Result<EndDeckCycleResult>.Failure(
                    $"Card instance not found: {cardInstanceId}");
            var resolvedTags = resolveCardTags(instance);
            if (resolvedTags.IsFailure)
                return Result<EndDeckCycleResult>.Failure(resolvedTags.Error);
            var tags = resolvedTags.Value;
            var reference = cardInstanceId.ToString();
            if (!string.IsNullOrWhiteSpace(policy.EtherealTag) &&
                tags.Contains(policy.EtherealTag, StringComparer.OrdinalIgnoreCase))
            {
                exhaust.Add(reference);
                continue;
            }

            var retained = tags.Any(tag =>
                policy.RetainTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            var shouldDiscard = policy.EndDiscard switch
            {
                DeckEndDiscardStrategy.None => false,
                DeckEndDiscardStrategy.All => true,
                DeckEndDiscardStrategy.NonRetain => !retained,
                DeckEndDiscardStrategy.DownToHandLimit =>
                    index < System.Math.Max(0, deck.Hand.Count - policy.HandLimit),
                _ => false
            };
            if (shouldDiscard)
                discard.Add(reference);
        }

        var current = deck;
        if (exhaust.Count > 0)
        {
            var moved = DeckTransitions.MoveFromHand(
                current,
                exhaust,
                CardConsumeDestination.Exhaust,
                context);
            if (moved.IsFailure)
                return Result<EndDeckCycleResult>.Failure(moved.Error);
            current = moved.Value.State;
            context = moved.Value.Context;
        }
        if (discard.Count > 0)
        {
            var moved = DeckTransitions.MoveFromHand(
                current,
                discard,
                CardConsumeDestination.Discard,
                context);
            if (moved.IsFailure)
                return Result<EndDeckCycleResult>.Failure(moved.Error);
            current = moved.Value.State;
            context = moved.Value.Context;
        }

        return Result<EndDeckCycleResult>.Success(new EndDeckCycleResult(
            current,
            context,
            discard,
            exhaust));
    }

    private static Result<CombatState> RefreshActorResource(
        CombatState combat,
        string actorId,
        ResourceCyclePolicyDefinition policy,
        RunState run)
    {
        if (!ScopeApplies(policy.ActorScope, combat, run, actorId) ||
            policy.StartActivation == ResourceRefreshStrategy.Preserve)
            return Result<CombatState>.Success(combat);

        var actor = combat.GetActor(actorId);
        if (actor == null)
            return Result<CombatState>.Failure($"Actor not found: {actorId}");
        var resource = actor.GetResource(policy.ResourceId);
        if (resource == null)
            return Result<CombatState>.Failure(
                $"Activation resource '{policy.ResourceId}' was not found on actor '{actorId}'");
        var value = policy.StartActivation switch
        {
            ResourceRefreshStrategy.ResetToMax => resource.Maximum,
            ResourceRefreshStrategy.Add => resource.Current + policy.Amount!.Value,
            ResourceRefreshStrategy.Set => policy.Amount!.Value,
            _ => resource.Current
        };
        var reduced = new ResourceMutationReducer().Apply(
            actor.ResourceState.Resources,
            [new ResolvedResourceMutation
            {
                MutationId = $"resource-cycle:start-activation:{actorId}:{policy.ResourceId}",
                ResourceId = policy.ResourceId,
                Operation = ResourceMutationOperation.Set,
                Value = value
            }]);
        if (reduced.IsFailure)
            return Result<CombatState>.Failure(reduced.Error);
        var resources = actor.ResourceState with { Resources = reduced.Value.Resources };
        return Result<CombatState>.Success(combat.ReplaceActor(actor.WithResourceState(resources)));
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
