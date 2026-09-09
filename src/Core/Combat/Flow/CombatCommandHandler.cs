using System.Text.Json;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Combat.Reactions;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;

namespace Core.Combat.Flow;

public sealed record CombatActionReduction
{
    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public CombatActionCommand? ResolvedCommand { get; init; }
    public string? ConsumedCardId { get; init; }
    public CardConsumeDestination Destination { get; init; }
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
}

/// <summary>
/// Applies state transitions already authorized by the legal-action boundary.
/// It owns no content lookup or controller policy and returns a new run/combat
/// pair without publishing either snapshot.
/// </summary>
public sealed class CombatActionStateReducer : ICombatActionStateReducer
{
    private readonly ICombatOutcomeResolver _outcomes;

    public CombatActionStateReducer(ICombatOutcomeResolver outcomes) =>
        _outcomes = outcomes ?? throw new ArgumentNullException(nameof(outcomes));

    public Result<CombatActionReduction> Apply(
        RunState run,
        CombatState previousCombat,
        LegalActionCandidate candidate,
        CombatActionCommand rootCommand,
        CombatFlowPoliciesDefinition policies)
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
        var destination = candidate.CardPlay?.Destination ?? CardConsumeDestination.None;
        if (candidate.CardPlay != null && consumedCardId != null && destination != CardConsumeDestination.None)
        {
            var moved = DeckTransitions.MoveFromHand(
                run.Deck,
                [consumedCardId],
                destination,
                run.Determinism);
            if (moved.IsFailure)
                return Result<CombatActionReduction>.Failure(moved.Error);
            run = run with { Deck = moved.Value.State, Determinism = moved.Value.Context };
        }
        else
        {
            consumedCardId = null;
            destination = CardConsumeDestination.None;
        }

        var combat = resolvedCommand == null
            ? candidate.SuccessorCombat
            : CombatFlowTransitions.ConsumeActionBudget(
                run,
                candidate.SuccessorCombat,
                resolvedCommand,
                policies.ActionBudget,
                CommandType(resolvedCommand));
        combat = _outcomes.Evaluate(
            combat,
            policies.Outcome,
            resolvedCommand?.ActorId ?? rootCommand.ActorId,
            CombatOutcomeEvaluationPoint.ActionResolution,
            resolvedCommand != null || candidate.ReactionTransition == ReactionTransitionKind.StackActionFizzled);
        run = ModifierTransitions.Tick(
            run,
            ModifierDurationBoundary.Command,
            previousCombat,
            rootCommand.ActorId,
            eligibleModifierIds);

        var activationBudgetExhausted =
            policies.ActionBudget.Strategy == ActionBudgetStrategy.FixedCount &&
            combat.ActivationState is { } activation &&
            activation.ActionsTaken >= policies.ActionBudget.MaxActionsPerActivation;
        var resolvedEndsTurn = (resolvedCommand ?? rootCommand).ActionType == ActionType.END_TURN;
        return Result<CombatActionReduction>.Success(new CombatActionReduction
        {
            Run = run,
            Combat = combat,
            ResolvedCommand = resolvedCommand,
            ConsumedCardId = consumedCardId,
            Destination = destination,
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
    public CombatResolutionStep Step { get; init; } = null!;
    public RunState NextRun { get; init; } = null!;
    public LegalActionCandidate Candidate { get; init; } = null!;
    public string? ConsumedCardId { get; init; }
    public CardConsumeDestination Destination { get; init; }
    public bool RequestsActivationAdvance { get; init; }
}

public interface ICombatCommandHandler
{
    Result<CombatCommandHandlingResult> Handle(CombatCommandHandlingRequest request);
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

    public Result<CombatCommandHandlingResult> Handle(CombatCommandHandlingRequest request)
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

        var legal = _legalActions.Evaluate(
            request.Run,
            request.Combat,
            request.Command,
            request.Origin);
        if (legal.IsFailure)
            return Result<CombatCommandHandlingResult>.Failure(legal.Error);
        if (!legal.Value.IsLegal)
            return Result<CombatCommandHandlingResult>.Failure(string.Join("; ", legal.Value.FailureReasons));

        var candidate = legal.Value.Candidate!;
        var reduced = _state.Apply(
            request.Run,
            request.Combat,
            candidate,
            request.Command,
            request.Run.ResolvedMode.CombatRules.Flow);
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
            Payload = JsonSerializer.SerializeToElement(new
            {
                combatId = request.CombatId,
                command = payloadCommand,
                decisionRuleId = request.DecisionRuleId,
                consumedCardId = reduced.Value.ConsumedCardId,
                destination = reduced.Value.Destination.ToString(),
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
            Step = step,
            NextRun = stepRun with { Determinism = stepRun.Determinism.AdvanceStep() },
            Candidate = candidate,
            ConsumedCardId = reduced.Value.ConsumedCardId,
            Destination = reduced.Value.Destination,
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
