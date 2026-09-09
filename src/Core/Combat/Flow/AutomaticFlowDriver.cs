using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Gambits;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Core.StatusEffects;

namespace Core.Combat.Flow;

public sealed record AutomaticFlowRequest
{
    public Guid CombatId { get; init; }
    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public bool RequestsActivationAdvance { get; init; }
}

public sealed record AutomaticFlowResult
{
    private ImmutableArray<CombatResolutionStep> _steps = [];

    public RunState Run { get; init; } = null!;
    public CombatState Combat { get; init; } = null!;
    public IReadOnlyList<CombatResolutionStep> Steps
    {
        get => _steps;
        init => _steps = value?.ToImmutableArray() ?? [];
    }
}

public interface IAutomaticFlowDriver
{
    Result<AutomaticFlowResult> Drive(AutomaticFlowRequest request);
}

/// <summary>
/// Drives the deterministic combat loop only while no player input is due.
/// Every automatic action uses the same command handler as REST input, while
/// activation lifecycle is delegated to the flow planner.
/// </summary>
public sealed class AutomaticFlowDriver : IAutomaticFlowDriver
{
    private readonly ICombatFlowPlanner _flow;
    private readonly IDecisionPolicyRegistry _decisions;
    private readonly ICombatCommandHandler _commands;

    public AutomaticFlowDriver(
        ICombatFlowPlanner flow,
        IDecisionPolicyRegistry decisions,
        ICombatCommandHandler commands)
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public Result<AutomaticFlowResult> Drive(AutomaticFlowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Run);
        ArgumentNullException.ThrowIfNull(request.Combat);
        if (request.Run.ResolvedMode == null)
            return Result<AutomaticFlowResult>.Failure("Run has no resolved game mode");

        var policies = request.Run.ResolvedMode.CombatRules.Flow;
        var steps = new List<CombatResolutionStep>();
        var run = request.Run;
        var combat = request.Combat;
        var automaticSteps = 0;

        var priority = DrivePriority(request.CombatId, run, combat, steps, ref automaticSteps, policies);
        if (priority.IsFailure)
            return Result<AutomaticFlowResult>.Failure(priority.Error);
        (run, combat, var priorityRequestsAdvance) = priority.Value;

        if ((request.RequestsActivationAdvance || priorityRequestsAdvance) && CanAdvanceActivation(combat))
        {
            var advanced = AdvanceActivation(run, combat, steps, ref automaticSteps, policies);
            if (advanced.IsFailure)
                return Result<AutomaticFlowResult>.Failure(advanced.Error);
            (run, combat) = advanced.Value;
        }

        while (combat.IsActive && combat.PriorityWindow == null &&
               combat.ActivationState is { WaitingForInput: false } activation)
        {
            if (automaticSteps >= policies.AutomaticResolution.MaxAutomaticSteps)
                return LimitFailure<AutomaticFlowResult>(policies.AutomaticResolution.MaxAutomaticSteps);
            if (string.IsNullOrWhiteSpace(activation.ActiveActorId))
                return Result<AutomaticFlowResult>.Failure("Automatic activation has no actor");
            var actor = combat.GetActor(activation.ActiveActorId);
            if (actor == null || combat.ControllerOf(actor) != ControllerKind.AI)
            {
                return Result<AutomaticFlowResult>.Failure(
                    $"Automatic activation actor is invalid: {activation.ActiveActorId}");
            }

            var lockedIntent = policies.Ai.Intent.Refresh == IntentRefreshStrategy.LockUntilActorActivation
                ? activation.Intents.FirstOrDefault(intent =>
                    string.Equals(intent.ActorId, actor.InstanceId, StringComparison.Ordinal))
                : null;
            var decision = lockedIntent == null
                ? _decisions.Decide(actor.ControllerBinding,
                    new DecisionPolicyRequest(run, combat, actor.InstanceId, policies.Ai.DecisionIds))
                : null;
            if (decision?.IsFailure == true)
                return Result<AutomaticFlowResult>.Failure(decision.Error);

            var command = lockedIntent?.ToCommand(run.RunId) ?? decision!.Value.Candidate.Command;
            var ruleId = lockedIntent?.RuleId ?? decision!.Value.RuleId;
            var action = HandleAutomatic(request.CombatId, run, combat, command, "combat.ai.action", ruleId);
            if (action.IsFailure && lockedIntent != null &&
                policies.Ai.Intent.WhenInvalid is InvalidIntentStrategy.Recompute or InvalidIntentStrategy.Hide)
            {
                decision = _decisions.Decide(actor.ControllerBinding,
                    new DecisionPolicyRequest(run, combat, actor.InstanceId, policies.Ai.DecisionIds));
                if (decision.IsFailure)
                    return Result<AutomaticFlowResult>.Failure(decision.Error);
                command = decision.Value.Candidate.Command;
                ruleId = decision.Value.RuleId;
                action = HandleAutomatic(
                    request.CombatId, run, combat, command, "combat.ai.action", ruleId);
            }
            if (action.IsFailure)
                return Result<AutomaticFlowResult>.Failure(action.Error);
            steps.Add(action.Value.Step);
            automaticSteps++;
            run = action.Value.NextRun;
            combat = action.Value.Step.Combat;
            if (!combat.IsActive)
                break;

            priority = DrivePriority(request.CombatId, run, combat, steps, ref automaticSteps, policies);
            if (priority.IsFailure)
                return Result<AutomaticFlowResult>.Failure(priority.Error);
            (run, combat, priorityRequestsAdvance) = priority.Value;
            if (combat.PriorityWindow != null)
                break;

            var requestsAdvance = action.Value.RequestsActivationAdvance || priorityRequestsAdvance;
            if (!requestsAdvance && command.ActionType != ActionType.END_TURN && policies.Ai.AutoEndAfterAction)
            {
                var endTurn = HandleAutomatic(
                    request.CombatId,
                    run,
                    combat,
                    new CombatActionCommand
                    {
                        RunId = run.RunId,
                        ActorId = actor.InstanceId,
                        ActionType = ActionType.END_TURN
                    },
                    "combat.ai.end_turn",
                    ruleId);
                if (endTurn.IsFailure)
                    return Result<AutomaticFlowResult>.Failure(endTurn.Error);
                steps.Add(endTurn.Value.Step);
                automaticSteps++;
                run = endTurn.Value.NextRun;
                combat = endTurn.Value.Step.Combat;
                requestsAdvance = true;
            }

            if (combat.IsActive && requestsAdvance && CanAdvanceActivation(combat))
            {
                var advanced = AdvanceActivation(run, combat, steps, ref automaticSteps, policies);
                if (advanced.IsFailure)
                    return Result<AutomaticFlowResult>.Failure(advanced.Error);
                (run, combat) = advanced.Value;
            }
        }

        if (!combat.IsActive && !combat.CompletedLifecycleBoundaries.Contains(CombatTriggerBoundaries.CombatEnd))
        {
            var completed = _flow.Complete(run, combat);
            if (completed.IsFailure)
                return Result<AutomaticFlowResult>.Failure(completed.Error);
            combat = completed.Value.Combat;
            run = completed.Value.Run ?? run;
            var effectSteps = completed.Value.Events.SelectMany(item => item.Steps).ToArray();
            steps.Add(new CombatResolutionStep
            {
                TransitionType = "combat.completed",
                Combat = combat,
                Deck = run.Deck,
                RunDeterminism = run.Determinism,
                RunSnapshot = run,
                EffectSteps = effectSteps,
                Calculations = effectSteps.Where(item => item.Calculation != null)
                    .Select(item => item.Calculation!).ToArray(),
                Applications = completed.Value.Events.SelectMany(item => item.Applications).ToArray(),
                Payload = JsonSerializer.SerializeToElement(new { events = completed.Value.Events })
            });
        }

        return Result<AutomaticFlowResult>.Success(new AutomaticFlowResult
        {
            Run = run,
            Combat = combat,
            Steps = steps
        });
    }

    private Result<(RunState Run, CombatState Combat, bool RequestsActivationAdvance)> DrivePriority(
        Guid combatId,
        RunState run,
        CombatState combat,
        ICollection<CombatResolutionStep> steps,
        ref int automaticSteps,
        CombatFlowPoliciesDefinition policies)
    {
        var requestsAdvance = false;
        while (combat.IsActive && combat.PriorityWindow is { } window)
        {
            var actor = combat.GetActor(window.HolderActorId);
            if (actor == null || !actor.IsAlive)
            {
                return Result<(RunState, CombatState, bool)>.Failure(
                    $"Priority holder is invalid: {window.HolderActorId}");
            }
            if (combat.ControllerOf(actor) != ControllerKind.AI)
                break;
            if (automaticSteps >= policies.AutomaticResolution.MaxAutomaticSteps)
                return LimitFailure<(RunState, CombatState, bool)>(policies.AutomaticResolution.MaxAutomaticSteps);

            var decision = _decisions.Decide(actor.ControllerBinding,
                new DecisionPolicyRequest(run, combat, actor.InstanceId, policies.Ai.DecisionIds));
            if (decision.IsFailure)
                return Result<(RunState, CombatState, bool)>.Failure(decision.Error);
            var action = HandleAutomatic(
                combatId,
                run,
                combat,
                decision.Value.Candidate.Command,
                "combat.priority.ai_action",
                decision.Value.RuleId);
            if (action.IsFailure)
                return Result<(RunState, CombatState, bool)>.Failure(action.Error);
            steps.Add(action.Value.Step);
            automaticSteps++;
            run = action.Value.NextRun;
            combat = action.Value.Step.Combat;
            requestsAdvance |= action.Value.RequestsActivationAdvance;
        }
        return Result<(RunState, CombatState, bool)>.Success((run, combat, requestsAdvance));
    }

    private Result<(RunState Run, CombatState Combat)> AdvanceActivation(
        RunState run,
        CombatState combat,
        ICollection<CombatResolutionStep> steps,
        ref int automaticSteps,
        CombatFlowPoliciesDefinition policies)
    {
        var plan = _flow.AdvanceActivation(run, combat, run.Deck, run.Determinism);
        if (plan.IsFailure)
            return Result<(RunState, CombatState)>.Failure(plan.Error);
        if (automaticSteps + plan.Value.Steps.Count > policies.AutomaticResolution.MaxAutomaticSteps)
            return LimitFailure<(RunState, CombatState)>(policies.AutomaticResolution.MaxAutomaticSteps);

        var context = run.Determinism;
        foreach (var step in plan.Value.Steps)
        {
            steps.Add(step);
            context = (step.RunDeterminism ?? context).AdvanceStep();
        }
        automaticSteps += plan.Value.Steps.Count;
        var latest = plan.Value.Steps.LastOrDefault(step => step.RunSnapshot != null)?.RunSnapshot ?? run;
        return Result<(RunState, CombatState)>.Success((
            latest with { Deck = plan.Value.Deck, Determinism = context },
            plan.Value.Combat));
    }

    private Result<CombatCommandHandlingResult> HandleAutomatic(
        Guid combatId,
        RunState run,
        CombatState combat,
        CombatActionCommand command,
        string transitionType,
        string? decisionRuleId) => _commands.Handle(new CombatCommandHandlingRequest
        {
            CombatId = combatId,
            Run = run,
            Combat = combat,
            Command = command,
            Origin = CombatCommandOrigin.AutomaticController,
            TransitionType = transitionType,
            DecisionRuleId = decisionRuleId
        });

    private static bool CanAdvanceActivation(CombatState combat) =>
        combat.IsActive && combat.PriorityWindow == null && combat.PendingActions.IsEmpty;

    private static Result<T> LimitFailure<T>(int maximum) =>
        Result<T>.Failure($"Automatic resolution exceeded {maximum} steps");
}
