using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Run;

namespace Core.Combat;

public sealed class CombatRunCoordinator : ICombatRunCoordinator
{
    private const string BasicAttackActionId = "basic_attack";

    private readonly ICombatSystem _combatSystem;
    private readonly IRunManager _runManager;
    private readonly IActionManager _actionManager;
    private readonly IScriptModifierManager? _scriptModifierManager;

    public CombatRunCoordinator(
        ICombatSystem combatSystem,
        IRunManager runManager,
        IActionManager actionManager,
        IScriptModifierManager? scriptModifierManager = null)
    {
        _combatSystem = combatSystem;
        _runManager = runManager;
        _actionManager = actionManager;
        _scriptModifierManager = scriptModifierManager;
    }

    public Result<CombatRunActionResult> ExecuteAction(Guid combatId, CombatActionCommand command)
    {
        if (command.RunId is not { } runId)
            return Result<CombatRunActionResult>.Failure("RunId is required for run-coordinated combat actions");

        if (command.ActionType is ActionType.PASS or ActionType.END_TURN)
            return ExecuteWithoutCardConsumption(combatId, runId, command);

        var cardId = ResolveCardId(command);
        if (string.IsNullOrWhiteSpace(cardId))
            return Result<CombatRunActionResult>.Failure("CardId is required for run-coordinated combat actions");

        var handResult = _runManager.HasCardInHand(runId, cardId);
        if (handResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(handResult.Error);

        if (!handResult.Value)
            return Result<CombatRunActionResult>.Failure($"Card '{cardId}' is not in run hand");

        var actionId = ResolveActionId(command, cardId);
        var actionResult = _actionManager.GetDefinition(actionId);
        if (actionResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(actionResult.Error);

        var actionDefinition = actionResult.Value;
        var destination = ResolveDestination(actionDefinition);
        var commandWithModifiers = command with
        {
            RunModifiers = ResolveRunModifiers(runId, actionDefinition.Tags)
        };
        var combatResult = _combatSystem.ExecuteAction(combatId, commandWithModifiers);
        if (combatResult == null)
            return Result<CombatRunActionResult>.Failure("Combat action was not executed");

        if (combatResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(combatResult.Error);

        if (destination != CardConsumeDestination.None)
        {
            var consumeResult = _runManager.ConsumeCardsFromHand(runId, new[] { cardId }, destination);
            if (consumeResult.IsFailure)
                return Result<CombatRunActionResult>.Failure(consumeResult.Error);
        }

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(runResult.Error);

        return Result<CombatRunActionResult>.Success(new CombatRunActionResult
        {
            CombatState = combatResult.Value,
            RunState = runResult.Value,
            ConsumedCardId = destination == CardConsumeDestination.None ? null : cardId,
            Destination = destination
        });
    }

    private Result<CombatRunActionResult> ExecuteWithoutCardConsumption(Guid combatId, Guid runId, CombatActionCommand command)
    {
        var combatResult = _combatSystem.ExecuteAction(combatId, command);
        if (combatResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(combatResult.Error);

        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(runResult.Error);

        return Result<CombatRunActionResult>.Success(new CombatRunActionResult
        {
            CombatState = combatResult.Value,
            RunState = runResult.Value,
            Destination = CardConsumeDestination.None
        });
    }

    private static string ResolveCardId(CombatActionCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.CardId))
            return command.CardId;

        return command.ActionType == ActionType.BASIC_ATTACK
            ? BasicAttackActionId
            : command.PowerId ?? string.Empty;
    }

    private static string ResolveActionId(CombatActionCommand command, string cardId)
    {
        return command.ActionType == ActionType.BASIC_ATTACK
            ? BasicAttackActionId
            : command.PowerId ?? cardId;
    }

    private static CardConsumeDestination ResolveDestination(ActionDefinition actionDefinition)
    {
        if (actionDefinition.Tags.Any(tag => string.Equals(tag, "retain", StringComparison.OrdinalIgnoreCase)))
            return CardConsumeDestination.None;

        if (actionDefinition.Tags.Any(tag => string.Equals(tag, "exhaust", StringComparison.OrdinalIgnoreCase)))
            return CardConsumeDestination.Exhaust;

        return CardConsumeDestination.Discard;
    }

    private IReadOnlyDictionary<string, float> ResolveRunModifiers(Guid runId, IEnumerable<string> tags)
    {
        return _scriptModifierManager?.GetPipelineModifiers($"run:{runId}", tags)
            ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    }
}
