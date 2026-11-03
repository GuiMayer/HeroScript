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
using Core.Effects;

namespace Core.Combat.Flow;

public sealed record CombatActionReduction
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal GameplayEffectObservation? Observation { get; init; }

    [System.Text.Json.Serialization.JsonIgnore] internal ImmutableArray<EffectExecutionStep> ResponseSteps { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore] internal ImmutableArray<EffectApplicationRecord> ResponseApplications { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore] internal ImmutableArray<Core.Calculations.CalculationResult> ResponseCalculations { get; init; } = [];
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
public sealed partial class CombatActionStateReducer : ICombatActionStateReducer
{
    private readonly ICombatOutcomeResolver _outcomes;
    private readonly ICardZoneFlowExecutor? _cardZoneFlows;
    private readonly IGameplayOccurrenceExecutor? _occurrences;

    public CombatActionStateReducer(
        ICombatOutcomeResolver outcomes,
        ICardZoneFlowExecutor? cardZoneFlows = null, IGameplayOccurrenceExecutor? occurrences = null)
    {
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));
        _cardZoneFlows = cardZoneFlows;
        _occurrences = occurrences;
    }

    public Result<CombatActionReduction> Apply(RunState run, CombatState previousCombat, LegalActionCandidate candidate,
        CombatActionCommand rootCommand, CombatFlowPoliciesDefinition policies) => ApplyCore(run, previousCombat, candidate, rootCommand, policies, null);

    private Result<CombatActionReduction> ApplyCore(RunState run, CombatState previousCombat, LegalActionCandidate candidate,
        CombatActionCommand rootCommand, CombatFlowPoliciesDefinition policies, GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(previousCombat);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(policies);

        var originalRun = run;
        var eligibleModifierIds = run.Modifiers.Select(item => item.InstanceId).ToHashSet();
        run = candidate.SuccessorRun;
        var currentCombat = candidate.SuccessorCombat;
        if (_occurrences != null)
        {
            var prepared = _occurrences.PrepareOccurrenceObservation(run, currentCombat, observation);
            if (prepared.IsFailure) return Result<CombatActionReduction>.Failure(prepared.Error);
            observation = prepared.Value;
        }
        var responseSteps = ImmutableArray.CreateBuilder<EffectExecutionStep>();
        var responseApplications = ImmutableArray.CreateBuilder<EffectApplicationRecord>();
        var responseCalculations = ImmutableArray.CreateBuilder<Core.Calculations.CalculationResult>();
        if (_occurrences != null && observation != null)
        {
            var previous = observation;
            var appended = GameplayActionLifecycleObservation.Append(observation, candidate);
            if (appended.IsFailure) return Result<CombatActionReduction>.Failure(appended.Error);
            observation = appended.Value;
            if (candidate.ReactionTransition == ReactionTransitionKind.Proposed && observation.Events.Occurrences.Count > previous.Events.Occurrences.Count)
            {
                var anchor = GameplayCausalAnchor.From(observation.Events.Occurrences.Last());
                currentCombat = currentCombat with { PendingActions = currentCombat.PendingActions.Select(item =>
                    item.PendingActionId == candidate.PendingAction!.PendingActionId ? item with { OriginCause = anchor } : item).ToImmutableArray() };
            }
            var response = _occurrences.DispatchOccurrencesObserved(originalRun, previousCombat, run, currentCombat, previous, observation);
            if (response.IsFailure) return Result<CombatActionReduction>.Failure(response.Error);
            Accept(response.Value);
        }
        var resolvedCommand = candidate.ResolvedCommand;
        var consumedCardId = resolvedCommand?.CardInstanceId?.ToString();
        ImmutableArray<CardZoneFlowStepRecord> cardZoneSteps = [];
        if (candidate.CardPlay != null && candidate.CardPlay.Evaluation.ZonePayments?.Any(payment => payment.IncludesResolvingCard) != true)
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
            var beforeCards = run; var previousObservation = observation;
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
            if (_occurrences != null && previousObservation != null && observation != null)
            {
                var response = _occurrences.DispatchOccurrencesObserved(beforeCards, currentCombat, run, currentCombat, previousObservation, observation);
                if (response.IsFailure) return Result<CombatActionReduction>.Failure(response.Error);
                Accept(response.Value);
            }
        }
        else if (candidate.CardPlay == null)
        {
            consumedCardId = null;
        }

        var combat = resolvedCommand == null
            ? currentCombat
            : CombatFlowTransitions.ConsumeActionBudget(
                run,
                currentCombat,
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
        var beforeTick = run; var beforeTickObservation = observation;
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

        if (_occurrences != null && beforeTickObservation != null && observation != null)
        {
            var response = _occurrences.DispatchOccurrencesObserved(beforeTick, combat, run, combat, beforeTickObservation, observation);
            if (response.IsFailure) return Result<CombatActionReduction>.Failure(response.Error);
            Accept(response.Value);
            if (response.Value.Execution.Steps.Length > 0)
            {
                var reevaluated = _outcomes.EvaluateWithContext(run, currentCombat, policies.Outcome,
                    resolvedCommand?.ActorId ?? rootCommand.ActorId, CombatOutcomeEvaluationPoint.ActionResolution,
                    resolvedCommand != null || candidate.ReactionTransition == ReactionTransitionKind.StackActionFizzled);
                if (reevaluated.IsFailure) return Result<CombatActionReduction>.Failure(reevaluated.Error);
                combat = reevaluated.Value;
            }
        }
        var activationBudgetExhausted =
            policies.ActionBudget.Strategy == ActionBudgetStrategy.FixedCount &&
            combat.ActivationState is { } activation &&
            activation.ActionsTaken >= policies.ActionBudget.MaxActionsPerActivation;
        var resolvedEndsTurn = (resolvedCommand ?? rootCommand).ActionType == ActionType.END_TURN;
        return Result<CombatActionReduction>.Success(new CombatActionReduction
        {
            Observation = observation, ResponseSteps = responseSteps.ToImmutable(), ResponseApplications = responseApplications.ToImmutable(),
            ResponseCalculations = responseCalculations.ToImmutable(), Run = run,
            Combat = combat,
            ResolvedCommand = resolvedCommand,
            ConsumedCardId = consumedCardId,
            CardZoneSteps = cardZoneSteps,
            RequestsActivationAdvance = resolvedEndsTurn || activationBudgetExhausted
        });

        void Accept(ObservedEffectExecution response)
        {
            run = response.Execution.Run ?? run; currentCombat = response.Execution.State; observation = response.Observation;
            responseSteps.AddRange(response.Execution.Steps); responseApplications.AddRange(response.Execution.Records);
            responseCalculations.AddRange(response.Execution.Calculations);
        }
    }

    private static string CommandType(CombatActionCommand command) => command.ActionType switch
    {
        ActionType.PLAY_CARD => GameplayCommandTypes.PlayCard,
        ActionType.END_TURN => GameplayCommandTypes.EndTurn,
        _ => GameplayCommandTypes.ExecuteAction
    };
}
