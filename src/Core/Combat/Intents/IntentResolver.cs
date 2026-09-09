using Core.Combat.Gambits;
using Core.Combat.Flow;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;

namespace Core.Combat.Intents;

public sealed class IntentResolver : IIntentResolver
{
    private readonly IDecisionPolicyRegistry _decisions;
    private readonly IActionManager _actions;
    private readonly ILegalActionResolver _legalActions;

    public IntentResolver(
        IDecisionPolicyRegistry decisions,
        IActionManager actions,
        ILegalActionResolver legalActions)
    {
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _legalActions = legalActions ?? throw new ArgumentNullException(nameof(legalActions));
    }

    public Result<CombatIntent> ResolveIntent(
        RunState run,
        CombatState combat,
        string actorId,
        IReadOnlyList<string> decisionIds)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(combat);
        if (string.IsNullOrWhiteSpace(actorId))
            return Result<CombatIntent>.Failure("Actor id is required");
        var actor = combat.GetActor(actorId);
        if (actor == null)
            return Result<CombatIntent>.Failure($"Actor not found: {actorId}");
        var decision = _decisions.Decide(actor.ControllerBinding,
            new DecisionPolicyRequest(run, combat, actorId, decisionIds));
        return decision.IsFailure
            ? Result<CombatIntent>.Failure(decision.Error)
            : Result<CombatIntent>.Success(BuildIntent(run, combat, decision.Value));
    }

    public Result<IReadOnlyList<CombatIntent>> ResolveEnemyIntents(
        RunState run,
        CombatState combat,
        IReadOnlyList<string> decisionIds,
        IntentPolicyDefinition policy)
    {
        var intents = new List<CombatIntent>();
        foreach (var actor in combat.GetAllActors()
                     .Where(actor => actor.IsAlive && combat.ControllerOf(actor) == ControllerKind.AI)
                     .OrderBy(actor => actor.InstanceId, StringComparer.Ordinal))
        {
            var actionCount = combat.ActionHistory.Count(action =>
                string.Equals(action.ActorId, actor.InstanceId, StringComparison.Ordinal));
            var existing = combat.ActivationState?.Intents.FirstOrDefault(intent =>
                string.Equals(intent.ActorId, actor.InstanceId, StringComparison.Ordinal));
            if (policy.Refresh == IntentRefreshStrategy.LockUntilActorActivation &&
                existing != null && existing.ActorActionCount == actionCount)
            {
                var validation = _legalActions.Evaluate(
                    run,
                    ProjectActivation(combat, actor.InstanceId),
                    existing.ToCommand(run.RunId),
                    CombatCommandOrigin.AutomaticController);
                if (validation.IsFailure)
                    return Result<IReadOnlyList<CombatIntent>>.Failure(validation.Error);
                if (validation.Value.IsLegal)
                {
                    intents.Add(existing);
                    continue;
                }
                if (policy.WhenInvalid == InvalidIntentStrategy.Fail)
                    return Result<IReadOnlyList<CombatIntent>>.Failure(
                        $"Locked intent became invalid for actor '{actor.InstanceId}': " +
                        string.Join("; ", validation.Value.FailureReasons));
                if (policy.WhenInvalid == InvalidIntentStrategy.Hide)
                    continue;
            }
            var intent = ResolveIntent(run, combat, actor.InstanceId, decisionIds);
            if (intent.IsFailure)
                return Result<IReadOnlyList<CombatIntent>>.Failure(intent.Error);
            intents.Add(intent.Value);
        }
        return Result<IReadOnlyList<CombatIntent>>.Success(intents);
    }

    private CombatIntent BuildIntent(RunState run, CombatState combat, DecisionPolicyResult decision)
    {
        var candidate = decision.Candidate;
        ActionDefinition? action = null;
        if (!string.IsNullOrWhiteSpace(candidate.ActionId))
        {
            var resolved = _actions is IRevisionedActionCatalog revisioned
                ? revisioned.GetDefinition(candidate.ActionId, run.Determinism.ContentRevision, run.ConfigName)
                : _actions.GetDefinition(candidate.ActionId);
            if (resolved.IsSuccess)
                action = resolved.Value;
        }
        return new CombatIntent
        {
            ActorId = candidate.Command.ActorId,
            ActionType = candidate.Command.ActionType,
            ActionId = candidate.ActionId,
            CardInstanceId = candidate.Command.CardInstanceId,
            CardDefinitionId = candidate.CardDefinitionId,
            TargetIds = candidate.Command.TargetIds.Count > 0
                ? candidate.Command.TargetIds
                : string.IsNullOrWhiteSpace(candidate.Command.TargetId) ? [] : [candidate.Command.TargetId],
            CostOptionId = candidate.Command.CostOptionId,
            DisplayName = decision.Intent.DisplayName ?? action?.DisplayName ?? candidate.Command.ActionType.ToString(),
            Description = decision.Intent.Description ?? action?.Description ?? string.Empty,
            TelegraphType = decision.Intent.TelegraphType ?? Telegraph(candidate.Command.ActionType, action),
            Tags = decision.Intent.Tags.Concat(action?.Tags ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            PreviewApplications = candidate.Applications,
            PreviewCalculations = candidate.Calculations,
            PreviewUncertain = candidate.OutcomeUncertain ||
                !string.Equals(combat.ActivationState?.ActiveActorId,
                    candidate.Command.ActorId,
                    StringComparison.Ordinal),
            PreviewFingerprint = candidate.ResolutionFingerprint,
            DecisionFingerprint = decision.DecisionFingerprint,
            StateFingerprint = decision.StateFingerprint,
            Priority = decision.Priority,
            PolicyId = decision.PolicyId,
            RuleId = decision.RuleId,
            ActorActionCount = combat.ActionHistory.Count(action =>
                string.Equals(action.ActorId, candidate.Command.ActorId, StringComparison.Ordinal))
        };
    }

    private static string Telegraph(ActionType type, ActionDefinition? action)
    {
        if (action?.Effects.Any(effect => effect.Type == EffectType.DAMAGE) == true) return "Attack";
        if (action?.Effects.Any(effect => effect.Type == EffectType.HEAL) == true) return "Heal";
        if (action?.Effects.Any(effect => effect.Type == EffectType.APPLY_STATUS) == true) return "Status";
        return type == ActionType.PASS ? "Pass" : type == ActionType.END_TURN ? "EndTurn" : "Action";
    }

    private static CombatState ProjectActivation(CombatState combat, string actorId) =>
        combat.PriorityWindow != null || combat.ActivationState == null ||
        string.Equals(combat.ActivationState.ActiveActorId, actorId, StringComparison.Ordinal)
            ? combat
            : combat with
            {
                ActivationState = combat.ActivationState with
                {
                    ActiveActorId = actorId,
                    WaitingForInput = false,
                    ActionsTaken = 0
                }
            };
}
