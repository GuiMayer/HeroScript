using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Reactions;
using Core.Common;
using Core.GameplayEvents;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Core.CardZones;

namespace Core.Combat.Flow;

public sealed record CombatActionReduction
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal GameplayEffectObservation? Observation { get; init; }

    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public CombatActionCommand? ResolvedCommand { get; init; }
    public string? ConsumedCardId { get; init; }
    public ImmutableArray<CardZoneFlowStepRecord> CardZoneSteps { get; init; } = [];
    public bool RequestsActivationAdvance { get; init; }
}

public interface ICombatActionStateReducer
{
    Result<CombatActionReduction> Apply(
        RunState run,
        CombatState previousCombat,
        LegalActionCandidate candidate,
        CombatActionCommand rootCommand,
        CombatFlowPoliciesDefinition policies);
    Result<ObservedGameplayExecution<CombatActionReduction>> ApplyObserved(RunState run, CombatState combat, LegalActionCandidate candidate,
        CombatActionCommand command, CombatFlowPoliciesDefinition policies, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatActionReduction>>.Failure("This reducer does not support gameplay observation");

}

/// <summary>
/// Applies state transitions already authorized by the legal-action boundary.
/// It owns no content lookup or controller policy and returns a new run/combat
/// pair without publishing either snapshot.
/// </summary>
public sealed class CombatActionStateReducer : ICombatActionStateReducer
{
    private readonly ICombatOutcomeResolver _outcomes;
    private readonly ICardZoneFlowExecutor? _cardZoneFlows;

    public CombatActionStateReducer(
        ICombatOutcomeResolver outcomes,
        ICardZoneFlowExecutor? cardZoneFlows = null)
    {
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
        _cardZoneFlows = cardZoneFlows;
    }

    public Result<CombatActionReduction> Apply(RunState run, CombatState previousCombat, LegalActionCandidate candidate,
        CombatActionCommand rootCommand, CombatFlowPoliciesDefinition policies) => ApplyCore(run, previousCombat, candidate, rootCommand, policies, null);

    public Result<ObservedGameplayExecution<CombatActionReduction>> ApplyObserved(RunState run, CombatState previousCombat, LegalActionCandidate candidate,
        CombatActionCommand rootCommand, CombatFlowPoliciesDefinition policies, GameplayEffectObservation observation)
    {
        var result = ApplyCore(run, previousCombat, candidate, rootCommand, policies, observation);
        return result.IsFailure ? Result<ObservedGameplayExecution<CombatActionReduction>>.Failure(result.Error)
            : Result<ObservedGameplayExecution<CombatActionReduction>>.Success(new(result.Value, result.Value.Observation!));
    }

    private Result<CombatActionReduction> ApplyCore(RunState run, CombatState previousCombat, LegalActionCandidate candidate,
        CombatActionCommand rootCommand, CombatFlowPoliciesDefinition policies, GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(previousCombat);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(policies);

        var eligibleModifierIds = run.Modifiers.Select(item => item.InstanceId).ToHashSet();
        run = candidate.SuccessorRun;
        var resolvedCommand = candidate.ResolvedCommand;
        var consumedCardId = resolvedCommand?.CardInstanceId?.ToString();
        ImmutableArray<CardZoneFlowStepRecord> cardZoneSteps = [];
        if (candidate.CardPlay != null)
        {
            if (consumedCardId == null)
                return Result<CombatActionReduction>.Failure("Played card instance id is required");
            var flowId = candidate.CardPlay.CardZoneResolutionFlowId;
            if (string.IsNullOrWhiteSpace(flowId))
                return Result<CombatActionReduction>.Failure(
                    $"Card {candidate.CardPlay.Card.DefinitionId} requires a card-zone resolution flow");
            var flowed = CardZoneRunFlowDispatcher.ResolveCard(_cardZoneFlows,
                run, run.Deck, run.Determinism, flowId,
                resolvedCommand?.ActorId ?? rootCommand.ActorId,
                Guid.Parse(consumedCardId), candidate.SuccessorCombat);
            if (flowed.IsFailure)
                return Result<CombatActionReduction>.Failure(flowed.Error);
            if (observation != null)
            {
                var observed = GameplayCardFlowObservation.Append(observation, flowed.Value.Steps, run,
                    run with { Deck = new DeckState { Topology = flowed.Value.State } }, resolvedCommand?.ActorId ?? rootCommand.ActorId);
                if (observed.IsFailure) return Result<CombatActionReduction>.Failure(observed.Error);
                observation = observed.Value;
            }
            run = run with
            {
                Deck = new DeckState { Topology = flowed.Value.State },
                Determinism = flowed.Value.Context
            };
            cardZoneSteps = flowed.Value.Steps;
        }
        else
        {
            consumedCardId = null;
        }

        var combat = resolvedCommand == null
            ? candidate.SuccessorCombat
            : CombatFlowTransitions.ConsumeActionBudget(
                run,
                candidate.SuccessorCombat,
                resolvedCommand,
                policies.ActionBudget,
                CommandType(resolvedCommand));
        if (resolvedCommand != null && combat.ActivationState is { } factsActivation && factsActivation.ActiveActorId == resolvedCommand.ActorId)
        {
            var facts = factsActivation.Facts;
            var actionId = resolvedCommand.PowerId ?? resolvedCommand.ActionType.ToString();
            facts = facts.SetItem("command:" + actionId, checked(facts.GetValueOrDefault("command:" + actionId) + 1));
            foreach (var step in cardZoneSteps.Concat(candidate.Applications.SelectMany(application => application.CardZoneSteps))
                .Where(step => step.Operation == CardZoneOperation.Move && step.PreviousStateHash != step.StateHash)) {
                var key = "moved:" + step.FlowId;
                facts = facts.SetItem(key, checked(facts.GetValueOrDefault(key) + step.InstanceIds.Length));
            }
            combat = combat with { ActivationState = factsActivation with { Facts = facts } };
        }
        var outcome = _outcomes.EvaluateWithContext(run,
            combat,
            policies.Outcome,
            resolvedCommand?.ActorId ?? rootCommand.ActorId,
            CombatOutcomeEvaluationPoint.ActionResolution,
            resolvedCommand != null || candidate.ReactionTransition == ReactionTransitionKind.StackActionFizzled);
        if (outcome.IsFailure) return Result<CombatActionReduction>.Failure(outcome.Error);
        combat = outcome.Value;
        var beforeTick = run;
        run = ModifierTransitions.Tick(
            run,
            ModifierDurationBoundary.Command,
            previousCombat,
            rootCommand.ActorId,
            eligibleModifierIds);
        if (observation != null)
        {
            var observed = GameplayModifierObservation.AppendTicks(observation, beforeTick, run);
            if (observed.IsFailure) return Result<CombatActionReduction>.Failure(observed.Error);
            observation = observed.Value;
        }

        var activationBudgetExhausted =
            policies.ActionBudget.Strategy == ActionBudgetStrategy.FixedCount &&
            combat.ActivationState is { } activation &&
            activation.ActionsTaken >= policies.ActionBudget.MaxActionsPerActivation;
        var resolvedEndsTurn = (resolvedCommand ?? rootCommand).ActionType == ActionType.END_TURN;
        return Result<CombatActionReduction>.Success(new CombatActionReduction
        {
            Observation = observation,
            Run = run,
            Combat = combat,
            ResolvedCommand = resolvedCommand,
            ConsumedCardId = consumedCardId,
            CardZoneSteps = cardZoneSteps,
            RequestsActivationAdvance = resolvedEndsTurn || activationBudgetExhausted
        });
    }

    private static string CommandType(CombatActionCommand command) => command.ActionType switch
    {
        ActionType.PLAY_CARD => GameplayCommandTypes.PlayCard,
        ActionType.END_TURN => GameplayCommandTypes.EndTurn,
        _ => GameplayCommandTypes.ExecuteAction
    };
}

public sealed record CombatCommandHandlingRequest
{
    public Guid CombatId { get; init; }
    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public CombatActionCommand Command { get; init; } = new();
    public CombatCommandOrigin Origin { get; init; }
    public string TransitionType { get; init; } = "combat.action.applied";
    public string? DecisionRuleId { get; init; }
}

public sealed record CombatCommandHandlingResult
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal GameplayEffectObservation? Observation { get; init; }

    public CombatResolutionStep Step { get; init; } = null!;
    public RunState NextRun { get; init; } = null!;
    public LegalActionCandidate Candidate { get; init; } = null!;
    public string? ConsumedCardId { get; init; }
    public bool RequestsActivationAdvance { get; init; }
}

public interface ICombatCommandHandler
{
    Result<CombatCommandHandlingResult> Handle(CombatCommandHandlingRequest request);
    Result<ObservedGameplayExecution<CombatCommandHandlingResult>> HandleObserved(CombatCommandHandlingRequest request, GameplayEffectObservation observation) =>
        Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure("This handler does not support gameplay observation");

}

/// <summary>
/// Canonical command composer shared by player input and automatic controllers.
/// Legality and action execution remain in ILegalActionResolver; state reduction
/// remains in ICombatActionStateReducer.
/// </summary>
public sealed class CombatCommandHandler : ICombatCommandHandler
{
    private readonly ILegalActionResolver _legalActions;
    private readonly ICombatActionStateReducer _state;

    public CombatCommandHandler(
        ILegalActionResolver legalActions,
        ICombatActionStateReducer state)
    {
        _legalActions = legalActions ?? throw new ArgumentNullException(nameof(legalActions));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public Result<CombatCommandHandlingResult> Handle(CombatCommandHandlingRequest request) => HandleCore(request, null);

    public Result<ObservedGameplayExecution<CombatCommandHandlingResult>> HandleObserved(CombatCommandHandlingRequest request, GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != request.Run.Determinism.ContentRevision)
            return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure("Command observation requires the action revision");
        var action = request.Command.PowerId ?? request.Command.ActionType.ToString();
        var started = GameplayActionObservation.Begin(observation, action, request.Command.ActorId);
        if (started.IsFailure) return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure(started.Error);
        var result = HandleCore(request, started.Value);
        if (result.IsFailure) return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure(result.Error);
        var completed = GameplayActionObservation.Complete(observation, started.Value, result.Value.Observation!, action, request.Command.ActorId);
        return completed.IsFailure ? Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure(completed.Error)
            : Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Success(new(result.Value with { Observation = completed.Value }, completed.Value));
    }

    private Result<CombatCommandHandlingResult> HandleCore(CombatCommandHandlingRequest request, GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Run);
        ArgumentNullException.ThrowIfNull(request.Combat);
        ArgumentNullException.ThrowIfNull(request.Command);
        if (request.Run.ResolvedMode == null)
            return Result<CombatCommandHandlingResult>.Failure("Run has no resolved game mode");
        if (request.Origin == CombatCommandOrigin.PlayerInput)
        {
            var input = CombatFlowTransitions.ValidateCommandInput(request.Combat, request.Command);
            if (input.IsFailure)
                return Result<CombatCommandHandlingResult>.Failure(input.Error);
        }

        Result<LegalActionEvaluation> legal;
        if (observation == null) legal = _legalActions.Evaluate(request.Run, request.Combat, request.Command, request.Origin);
        else
        {
            var observed = _legalActions.EvaluateObserved(request.Run, request.Combat, request.Command, request.Origin, observation);
            if (observed.IsFailure) return Result<CombatCommandHandlingResult>.Failure(observed.Error);
            observation = observed.Value.Observation;
            legal = Result<LegalActionEvaluation>.Success(observed.Value.Execution);
        }
        if (legal.IsFailure)
            return Result<CombatCommandHandlingResult>.Failure(legal.Error);
        if (!legal.Value.IsLegal)
            return Result<CombatCommandHandlingResult>.Failure(string.Join("; ", legal.Value.FailureReasons));

        var candidate = legal.Value.Candidate!;
        Result<CombatActionReduction> reduced;
        if (observation == null) reduced = _state.Apply(request.Run, request.Combat, candidate, request.Command, request.Run.ResolvedMode.CombatRules.Flow);
        else
        {
            var observed = _state.ApplyObserved(request.Run, request.Combat, candidate, request.Command, request.Run.ResolvedMode.CombatRules.Flow, observation);
            if (observed.IsFailure) return Result<CombatCommandHandlingResult>.Failure(observed.Error);
            observation = observed.Value.Observation;
            reduced = Result<CombatActionReduction>.Success(observed.Value.Execution);
        }
        if (reduced.IsFailure)
            return Result<CombatCommandHandlingResult>.Failure(reduced.Error);

        var payloadCommand = candidate.Command with
        {
            IgnoreConfiguredCosts = request.Run.ResolvedMode.CombatRules.Flow.ActionBudget.ActionCosts ==
                                    ActionCostStrategy.Ignore,
            DeferTurnLifecycle = true
        };
        var stepRun = reduced.Value.Run;
        var step = new CombatResolutionStep
        {
            TransitionType = TransitionType(candidate, request.TransitionType),
            Combat = reduced.Value.Combat,
            Deck = stepRun.Deck,
            RunDeterminism = stepRun.Determinism,
            RunSnapshot = stepRun,
            EffectSteps = candidate.Steps,
            Calculations = candidate.Calculations,
            Applications = candidate.Applications,
            CardZoneSteps = reduced.Value.CardZoneSteps,
            Payload = JsonSerializer.SerializeToElement(new
            {
                combatId = request.CombatId,
                command = payloadCommand,
                decisionRuleId = request.DecisionRuleId,
                consumedCardId = reduced.Value.ConsumedCardId,
                cardZoneResolutionFlowId = candidate.CardPlay?.CardZoneResolutionFlowId,
                cardResolution = candidate.CardPlay == null ? null : new
                {
                    cardInstanceId = candidate.CardPlay.Card.CardInstanceId,
                    definitionId = candidate.CardPlay.Card.DefinitionId,
                    fingerprint = candidate.CardPlay.Card.Fingerprint,
                    resolutionFingerprint = candidate.CardPlay.ResolutionFingerprint,
                    calculations = candidate.CardPlay.Calculations,
                    applications = candidate.CardPlay.Applications,
                    steps = candidate.CardPlay.Steps
                },
                abilityResolution = candidate.Ability == null ? null : new
                {
                    actionId = candidate.Ability.Definition.ActionId,
                    resolutionFingerprint = candidate.Ability.ResolutionFingerprint,
                    calculations = candidate.Ability.Calculations,
                    applications = candidate.Ability.Applications,
                    steps = candidate.Ability.Steps
                },
                phaseTransitions = candidate.PhaseTransitions,
                reactionTransition = candidate.ReactionTransition,
                pendingAction = candidate.PendingAction,
                resolvedCommand = reduced.Value.ResolvedCommand,
                resolutionFingerprint = candidate.ResolutionFingerprint
            })
        };
        return Result<CombatCommandHandlingResult>.Success(new CombatCommandHandlingResult
        {
            Observation = observation,
            Step = step,
            NextRun = stepRun with { Determinism = stepRun.Determinism.AdvanceStep() },
            Candidate = candidate,
            ConsumedCardId = reduced.Value.ConsumedCardId,
            RequestsActivationAdvance = reduced.Value.RequestsActivationAdvance
        });
    }

    private static string TransitionType(LegalActionCandidate candidate, string fallback) =>
        candidate.ReactionTransition switch
        {
            ReactionTransitionKind.Proposed => "combat.reaction.proposed",
            ReactionTransitionKind.PriorityPassed => "combat.priority.passed",
            ReactionTransitionKind.StackActionResolved => "combat.reaction.resolved",
            ReactionTransitionKind.StackActionFizzled => "combat.reaction.fizzled",
            _ => fallback
        };
}
