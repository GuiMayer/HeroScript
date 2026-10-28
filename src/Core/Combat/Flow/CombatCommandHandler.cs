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
public sealed partial class CombatCommandHandler : ICombatCommandHandler
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
