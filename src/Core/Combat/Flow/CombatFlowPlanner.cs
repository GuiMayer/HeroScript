using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Combat.TurnPhase;
using Core.Common;
using Core.Content;
using Core.Determinism;
using Core.Run;

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

    public CombatFlowPlanner(IContentRuntimeResolver contentRuntimes, IActionManager actions)
    {
        _contentRuntimes = contentRuntimes ?? throw new ArgumentNullException(nameof(contentRuntimes));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public Result<CombatState> Initialize(RunState run, CombatState combat)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        var context = ResolveContext(run);
        return context.IsFailure
            ? Result<CombatState>.Failure(context.Error)
            : Initialize(run, combat, context.Value.Sequence, context.Value.Policies);
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

        return AdvanceActivation(
            run,
            combat,
            deck,
            runDeterminism,
            context.Value.Sequence,
            context.Value.Policies,
            actionId => ResolveAction(run, actionId));
    }

    public static Result<CombatState> Initialize(
        RunState run,
        CombatState combat,
        PhaseSequenceDefinition sequence,
        CombatFlowPoliciesDefinition policies)
    {
        var validation = PhaseSequenceLoader.ValidateSequence(sequence);
        if (validation.IsFailure)
            return Result<CombatState>.Failure(validation.Error);

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
            PhaseState = CreatePhaseState(sequence, middle, actorId, order)
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
        var activation = combat.ActivationState;
        if (activation == null || string.IsNullOrWhiteSpace(activation.ActiveActorId))
            return Result<CombatFlowAdvanceResult>.Failure("Combat activation has not been initialized");
        if (!combat.IsActive)
            return Result<CombatFlowAdvanceResult>.Failure("A terminal combat cannot advance activation");

        var endedDeck = ApplyEndDeckCycle(
            run,
            combat,
            deck,
            activation.ActiveActorId,
            policies.DeckCycle,
            runDeterminism,
            resolveAction);
        if (endedDeck.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(endedDeck.Error);

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
            PhaseState = CreatePhaseState(
                sequence,
                endPhase,
                activation.ActiveActorId,
                activation.ActivationOrder)
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
            return Result<CombatFlowAdvanceResult>.Failure("No alive actors are available for the next activation");

        var nextActorId = nextOrder[nextIndex];
        var refreshed = RefreshActorResource(endedCombat, nextActorId, policies.ResourceCycle, run);
        if (refreshed.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(refreshed.Error);

        // The end transition will advance the run clock once before the draw.
        var drawContext = endedDeck.Value.Context.AdvanceStep();
        var startedDeck = ApplyStartDeckCycle(
            run,
            refreshed.Value,
            endedDeck.Value.State,
            nextActorId,
            policies.DeckCycle,
            drawContext);
        if (startedDeck.IsFailure)
            return Result<CombatFlowAdvanceResult>.Failure(startedDeck.Error);

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
            PhaseState = CreatePhaseState(sequence, middle, nextActorId, nextOrder)
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

        return Result<CombatFlowAdvanceResult>.Success(new CombatFlowAdvanceResult
        {
            Steps = [endStep, startStep]
        });
    }

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
            var cardId = deck.Hand[index];
            var definition = resolveAction(cardId);
            if (definition.IsFailure)
                return Result<EndDeckCycleResult>.Failure(definition.Error);
            var tags = definition.Value.Tags;
            var reference = deck.InstanceTrackingEnabled
                ? deck.HandInstanceIds[index].ToString()
                : cardId;
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
        var refreshed = policy.StartActivation switch
        {
            ResourceRefreshStrategy.ResetToMax => resource.Reset(),
            ResourceRefreshStrategy.Add => resource.Gain(policy.Amount!.Value),
            ResourceRefreshStrategy.Set => resource.Set(policy.Amount!.Value),
            _ => resource
        };
        return Result<CombatState>.Success(combat.ReplaceEntity(actor.UpdateResource(policy.ResourceId, refreshed)));
    }

    private static PhaseState CreatePhaseState(
        PhaseSequenceDefinition sequence,
        PhaseDefinition phase,
        string actorId,
        IReadOnlyList<string> order) => new()
        {
            CurrentPhaseId = phase.PhaseId,
            PhaseIndex = sequence.Phases.ToList().IndexOf(phase),
            PhaseSequence = sequence,
            PriorityOrder = order,
            CurrentPriorityIndex = System.Math.Max(0, order.ToList().IndexOf(actorId)),
            ActivePlayerId = actorId,
            CanTransition = phase.AutoTransition,
            PhaseStartTime = DateTime.UnixEpoch
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
