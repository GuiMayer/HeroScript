using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Activation;
using Core.Combat.Intents;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;

namespace Core.Combat.Flow;

public sealed record CombatInitializationResult(CombatState Combat, RunState Run);

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

/// <summary>
/// Pure planner for the canonical actor-activation loop. It reads only the
/// run's pinned content revision and returns immutable transition snapshots;
/// publication is left to the run aggregate's atomic batch commit.
/// </summary>
public sealed class CombatFlowPlanner : ICombatFlowPlanner
{
    private readonly IContentRuntimeResolver _contentRuntimes;
    private readonly IActionManager _actions;
    private readonly IIntentResolver _intents;
    private readonly ICombatStatusLifecycle _statusLifecycle;
    private readonly ICombatRelicLifecycle _relicLifecycle;
    private readonly ICombatResourceLifecycle _resourceLifecycle;

    public CombatFlowPlanner(
        IContentRuntimeResolver contentRuntimes,
        IActionManager actions,
        IIntentResolver intents,
        ICombatStatusLifecycle statusLifecycle,
        ICombatRelicLifecycle relicLifecycle,
        ICombatResourceLifecycle resourceLifecycle)
    {
        _contentRuntimes = contentRuntimes ?? throw new ArgumentNullException(nameof(contentRuntimes));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _statusLifecycle = statusLifecycle ?? throw new ArgumentNullException(nameof(statusLifecycle));
        _relicLifecycle = relicLifecycle ?? throw new ArgumentNullException(nameof(relicLifecycle));
        _resourceLifecycle = resourceLifecycle ?? throw new ArgumentNullException(nameof(resourceLifecycle));
    }

    public Result<CombatState> Initialize(RunState run, CombatState combat)
    {
        var result = InitializeTransaction(run, combat);
        return result.IsFailure ? Result<CombatState>.Failure(result.Error) : Result<CombatState>.Success(result.Value.Combat);
    }

    public Result<CombatInitializationResult> InitializeTransaction(RunState run, CombatState combat)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        var context = ResolveContext(run);
        if (context.IsFailure) return Result<CombatInitializationResult>.Failure(context.Error);
        var deck = DeckTransitions.BeginEncounter(run.Deck, context.Value.Policies.DeckCycle, run.Determinism);
        if (deck.IsFailure) return Result<CombatInitializationResult>.Failure(deck.Error);
        run = run with { Deck = deck.Value.State, Determinism = deck.Value.Context };
        var initialized = Initialize(run, combat, context.Value.Sequence, context.Value.Policies);
        if (initialized.IsFailure) return Result<CombatInitializationResult>.Failure(initialized.Error);
        var regenerated = ApplyResourceLifecycle(run, initialized.Value,
            initialized.Value.ActivationState!.ActiveActorId!, RegenerationTiming.START_TURN, context.Value.Policies);
        if (regenerated.IsFailure) return Result<CombatInitializationResult>.Failure(regenerated.Error);
        run = regenerated.Value.Run ?? run;
        var relics = _relicLifecycle.Process(run, regenerated.Value.Combat, CombatTriggerBoundaries.CombatStart);
        if (relics.IsFailure) return Result<CombatInitializationResult>.Failure(relics.Error);
        run = relics.Value.Run ?? run;
        var afterRelics = CombatFlowTransitions.EvaluateOutcome(relics.Value.Combat,
            context.Value.Policies.Outcome, initialized.Value.ActivationState?.ActiveActorId);
        var initial = ApplyInitialLifecycle(run, afterRelics, context.Value.Policies);
        if (initial.IsFailure) return initial;
        run = initial.Value.Run;
        if (!initial.Value.Combat.IsActive)
        {
            var completed = Complete(run, initial.Value.Combat);
            return completed.IsFailure ? Result<CombatInitializationResult>.Failure(completed.Error)
                : Result<CombatInitializationResult>.Success(new(completed.Value.Combat, completed.Value.Run ?? run));
        }
        var intents = PublishIntents(run, initial.Value.Combat, context.Value.Policies);
        return intents.IsFailure ? Result<CombatInitializationResult>.Failure(intents.Error)
            : Result<CombatInitializationResult>.Success(new(intents.Value, run));
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
        DeterministicContext runDeterminism)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(runDeterminism);
        var context = ResolveContext(run);
        if (context.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(context.Error);

        var advanced = AdvanceWithLifecycle(
            run,
            combat,
            deck,
            runDeterminism,
            context.Value.Sequence,
            context.Value.Policies);
        if (advanced.IsFailure)
            return advanced;

        var published = PublishIntents(run, advanced.Value.Combat, context.Value.Policies);
        if (published.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(published.Error);
        var steps = advanced.Value.Steps.ToArray();
        steps[^1] = steps[^1] with { Combat = published.Value };
        return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult { Steps = steps });
    }

    private Result<CombatFlowAdvanceResult> AdvanceWithLifecycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies)
    {
        var planned = EndActivation(
            run,
            combat,
            deck,
            runDeterminism,
            sequence,
            policies,
            actionId => ResolveAction(run, actionId));
        if (planned.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(planned.Error);

        var steps = new List<CombatResolutionStep> { planned.Value };
        var current = planned.Value.Combat;
        var endedDeck = planned.Value.Deck;
        run = run with { Deck = endedDeck, Determinism = planned.Value.RunDeterminism!.AdvanceStep() };
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

        var recalculated = StartActivation(run, current, endedDeck, run.Determinism, sequence, policies);
        if (recalculated.IsFailure) return Result<CombatFlowAdvanceResult>.Failure(recalculated.Error);
        var startedStep = recalculated.Value;
        current = startedStep.Combat with
        {
            CurrentTurn = startedStep.Combat.ActivationState!.Round
        };
        run = run with { Deck = startedStep.Deck, Determinism = startedStep.RunDeterminism! };

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

        var evaluated = CombatFlowTransitions.EvaluateOutcome(
            processed.Value.Combat,
            policies.Outcome,
            actorId);
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

    public static Result<CombatState> Initialize(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies)
    {
        var validation = PhaseSequenceValidator.ValidateCanonicalActivationSequence(sequence);
        if (validation.IsFailure)
            return Result<CombatState>.Failure(validation.Error);

        if (run.Scenario is { } scenario)
        {
            combat = combat with
            {
                Relationships = scenario.Relationships,
                Sides = scenario.Sides,
                Hero = combat.Hero with { SideId = scenario.Hero.SideId },
                Enemies = combat.Enemies.Select(entity => entity with
                {
                    SideId = scenario.Enemies.First(item => item.Alias == entity.EntityId).SideId
                }).ToArray()
            };
        }

        var order = CombatFlowTransitions.CreateRoundSnapshotOrder(combat, policies.ActivationOrder);
        if (order.Count == 0)
            return Result<CombatState>.Failure("No alive actors are available for combat activation");

        var actorId = order[0];
        var refreshed = RefreshActorResource(combat, actorId, policies.ResourceCycle, run);
        if (refreshed.IsFailure)
            return Result<CombatState>.Failure(refreshed.Error);

        var middle = sequence.Phases.First(phase => phase.Role == PhaseRole.Middle);
        var activation = new ActivationState
        {
            ActiveActorId = actorId,
            Round = 1,
            ActivationIndex = 0,
            ActivationNumber = 1,
            ActionsTaken = 0,
            ActivationOrder = order,
            CompletedActorIds = [],
            WaitingForInput = IsPlayerActor(refreshed.Value, run, actorId),
            RunId = run.RunId,
            StartedAtUtc = refreshed.Value.Determinism.LogicalTimestamp.UtcDateTime
        };

        return Result<CombatState>.Success(refreshed.Value with
        {
            ActivationState = activation,
            PhaseState = CreatePhaseState(sequence, middle)
        });
    }

    public static Result<CombatFlowAdvanceResult> AdvanceActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        Func<string, Result<ActionDefinition>> resolveAction)
    {
        var ended = EndActivation(run, combat, deck, runDeterminism, sequence, policies, resolveAction);
        if (ended.IsFailure) return Result<CombatFlowAdvanceResult>.Failure(ended.Error);
        var started = StartActivation(run, ended.Value.Combat, ended.Value.Deck,
            ended.Value.RunDeterminism!.AdvanceStep(), sequence, policies);
        return started.IsFailure ? Result<CombatFlowAdvanceResult>.Failure(started.Error)
            : Result<CombatFlowAdvanceResult>.Success(new() { Steps = [ended.Value, started.Value] });
    }

    private static Result<CombatResolutionStep> EndActivation(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext runDeterminism,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies,
        Func<string, Result<ActionDefinition>> resolveAction)
    {
        var activation = combat.ActivationState;
        if (activation == null || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return Result<CombatResolutionStep>.Failure("Combat activation has not been initialized");
        if (!combat.IsActive)
            return Result<CombatResolutionStep>.Failure("A terminal combat cannot advance activation");

        var endedDeck = ApplyEndDeckCycle(
            run,
            combat,
            deck,
            activation.ActiveActorId,
            policies.DeckCycle,
            runDeterminism,
            resolveAction);
        if (endedDeck.IsFailure)
            return Result<CombatResolutionStep>.Failure(endedDeck.Error);

        var completed = activation.CompletedActorIds
            .Append(activation.ActiveActorId)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var endPhase = sequence.Phases.First(phase => phase.Role == PhaseRole.End);
        var endedCombat = combat with
        {
            ActivationState = activation with
            {
                CompletedActorIds = completed,
                WaitingForInput = false
            },
            PhaseState = CreatePhaseState(sequence, endPhase)
        };
        var endStep = new CombatResolutionStep
        {
            TransitionType = "combat.activation.ended",
            Combat = endedCombat,
            Deck = endedDeck.Value.State,
            RunDeterminism = endedDeck.Value.Context,
            Payload = JsonSerializer.SerializeToElement(new
            {
                actorId = activation.ActiveActorId,
                activation.Round,
                activation.ActivationNumber,
                discardedCardIds = endedDeck.Value.Discarded,
                exhaustedCardIds = endedDeck.Value.Exhausted
            })
        };

        return Result<CombatResolutionStep>.Success(endStep);
    }

    private static Result<CombatResolutionStep> StartActivation(
        RunState run, CombatState endedCombat, DeckState deck, DeterministicContext drawContext,
        PhaseSequenceDefinition sequence, CombatFlowPoliciesDefinition policies)
    {
        var activation = endedCombat.ActivationState!;
        var completed = activation.CompletedActorIds;
        var combat = endedCombat;
        var eligibleOrder = activation.ActivationOrder
            .Where(actorId => combat.GetEntity(actorId)?.IsAlive == true)
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
            nextOrder = CombatFlowTransitions.CreateRoundSnapshotOrder(endedCombat, policies.ActivationOrder);
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

        var middle = sequence.Phases.First(phase => phase.Role == PhaseRole.Middle);
        var nextActivation = activation with
        {
            ActiveActorId = nextActorId,
            Round = round,
            ActivationIndex = nextIndex,
            ActivationNumber = checked(activation.ActivationNumber + 1),
            ActionsTaken = 0,
            ActivationOrder = nextOrder,
            CompletedActorIds = nextCompleted,
            WaitingForInput = IsPlayerActor(refreshed.Value, run, nextActorId),
            StartedAtUtc = refreshed.Value.Determinism.LogicalTimestamp.UtcDateTime
        };
        var startedCombat = refreshed.Value with
        {
            ActivationState = nextActivation,
            PhaseState = CreatePhaseState(sequence, middle)
        };
        var startStep = new CombatResolutionStep
        {
            TransitionType = "combat.activation.started",
            Combat = startedCombat,
            Deck = startedDeck.Value.State,
            RunDeterminism = startedDeck.Value.Context,
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

    private Result<CombatInitializationResult> ApplyInitialLifecycle(
        RunState run,
        CombatState combat,
        CombatFlowPoliciesDefinition policies)
    {
        var current = combat;
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
            current = CombatFlowTransitions.EvaluateOutcome(
                relics.Value.Combat,
                policies.Outcome,
                current.ActivationState?.ActiveActorId);
            if (!current.IsActive)
                break;
        }
        return Result<CombatInitializationResult>.Success(new(current, run));
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
        var evaluated = CombatFlowTransitions.EvaluateOutcome(
            relics.Value.Combat,
            policies.Outcome,
            activeActorId);
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
            .Where(actorId => combat.GetEntity(actorId)?.IsAlive == true)
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

    private Result<(PhaseSequenceDefinition Sequence, CombatFlowPoliciesDefinition Policies)> ResolveContext(
        RunState run)
    {
        var combatRules = run.ResolvedMode?.CombatRules;
        if (combatRules == null)
            return Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition)>.Failure(
                "Run has no resolved combat rules");
        if (string.IsNullOrWhiteSpace(combatRules.DefaultPhaseSequenceId))
            return Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition)>.Failure(
                "Combat rules have no phase sequence id");

        var runtime = _contentRuntimes.Resolve(run.Determinism.ContentRevision, run.ConfigName);
        if (runtime.IsFailure)
            return Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition)>.Failure(runtime.Error);
        var sequence = runtime.Value.GetDefinition<PhaseSequenceDefinition>(
            "phase-sequences",
            combatRules.DefaultPhaseSequenceId);
        return sequence.IsFailure
            ? Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition)>.Failure(sequence.Error)
            : Result<(PhaseSequenceDefinition, CombatFlowPoliciesDefinition)>.Success(
                (sequence.Value, combatRules.Flow));
    }

    private Result<ActionDefinition> ResolveAction(RunState run, string actionId) =>
        _actions is IRevisionedActionCatalog revisioned
            ? revisioned.GetDefinition(actionId, run.Determinism.ContentRevision, run.ConfigName)
            : _actions.GetDefinition(actionId);

    private Result<CombatState> PublishIntents(
        RunState run,
        CombatState combat,
        CombatFlowPoliciesDefinition policies)
    {
        var activation = combat.ActivationState;
        if (activation == null)
            return Result<CombatState>.Failure("Combat activation has not been initialized");
        if (!policies.Ai.PublishIntents)
        {
            return Result<CombatState>.Success(combat with
            {
                ActivationState = activation with { Intents = [] }
            });
        }

        var resolved = _intents.ResolveEnemyIntents(
            combat,
            run.RunId,
            policies.Ai.GambitIds.Count == 0 ? null : policies.Ai.GambitIds);
        return resolved.IsFailure
            ? Result<CombatState>.Failure(resolved.Error)
            : Result<CombatState>.Success(combat with
            {
                ActivationState = activation with { Intents = resolved.Value }
            });
    }

    private static Result<ResourceRefreshResult> ApplyStartDeckCycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        string actorId,
        DeckCyclePolicyDefinition policy,
        DeterministicContext context)
    {
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

    private static Result<EndDeckCycleResult> ApplyEndDeckCycle(
        RunState run,
        CombatState combat,
        DeckState deck,
        string actorId,
        DeckCyclePolicyDefinition policy,
        DeterministicContext context,
        Func<string, Result<ActionDefinition>> resolveAction)
    {
        if (!ScopeApplies(policy.ActorScope, combat, run, actorId) || deck.Hand.Count == 0)
            return Result<EndDeckCycleResult>.Success(new EndDeckCycleResult(deck, context, [], []));

        var exhaust = new List<string>();
        var discard = new List<string>();
        for (var index = 0; index < deck.Hand.Count; index++)
        {
            var cardInstanceId = deck.HandInstanceIds[index];
            var cardId = deck.GetDefinitionId(cardInstanceId);
            if (cardId == null)
                return Result<EndDeckCycleResult>.Failure(
                    $"Card instance not found: {cardInstanceId}");
            var definition = resolveAction(cardId);
            if (definition.IsFailure)
                return Result<EndDeckCycleResult>.Failure(definition.Error);
            var tags = definition.Value.Tags;
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

        var actor = combat.GetEntity(actorId);
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
        return Result<CombatState>.Success(combat.ReplaceEntity(actor with { ResourceState = resources }));
    }

    private static PhaseState CreatePhaseState(
        PhaseSequenceDefinition sequence,
        PhaseDefinition phase) => new()
        {
            CurrentPhaseId = phase.PhaseId,
            PhaseIndex = sequence.Phases.ToList().IndexOf(phase),
            PhaseSequence = sequence
        };

    private static bool ScopeApplies(
        FlowActorScope scope,
        CombatState combat,
        RunState run,
        string actorId)
    {
        var isPlayer = IsPlayerActor(combat, run, actorId);
        return scope switch
        {
            FlowActorScope.Player => isPlayer,
            FlowActorScope.Enemies => !isPlayer,
            FlowActorScope.All => true,
            _ => false
        };
    }

    private static bool IsPlayerActor(CombatState combat, RunState run, string actorId) =>
        string.Equals(actorId, run.PlayerEntityId, StringComparison.Ordinal) ||
        string.Equals(actorId, combat.Hero.EntityId, StringComparison.Ordinal);

    private sealed record EndDeckCycleResult(
        DeckState State,
        DeterministicContext Context,
        IReadOnlyList<string> Discarded,
        IReadOnlyList<string> Exhausted);

    private sealed record ResourceRefreshResult(
        DeckState State,
        DeterministicContext Context,
        IReadOnlyList<string> Cards);
}
