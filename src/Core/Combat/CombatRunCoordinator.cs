using Core.Combat.Models;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Run;
using System.Collections.Concurrent;

namespace Core.Combat;

public sealed class CombatRunCoordinator : ICombatRunCoordinator
{
    private const string BasicAttackActionId = "basic_attack";

    private readonly ICombatSystem _combatSystem;
    private readonly IRunManager _runManager;
    private readonly IActionManager _actionManager;
    private readonly IScriptModifierManager? _scriptModifierManager;
    private readonly ConcurrentDictionary<Guid, object> _runLocks = new();

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

    public Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        string heroId,
        IReadOnlyList<string> enemyIds,
        int initialEnergy = 3)
    {
        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var runResult = _runManager.GetRun(runId);
            if (runResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(runResult.Error);

            var run = runResult.Value;
            if (run.GetActiveEncounter() != null)
                return Result<CombatRunEncounterResult>.Failure(
                    $"Run already has an active encounter: {run.ActiveEncounterId}");
            if (run.CurrentNodeId == null)
                return Result<CombatRunEncounterResult>.Failure("Run has no current map node");

            var node = run.Map.Nodes.FirstOrDefault(item =>
                string.Equals(item.NodeId, run.CurrentNodeId, StringComparison.Ordinal));
            if (node == null)
                return Result<CombatRunEncounterResult>.Failure($"Map node not found: {run.CurrentNodeId}");
            if (!RunMapTransitions.IsEncounterNode(node.NodeType))
                return Result<CombatRunEncounterResult>.Failure($"Current map node is not an encounter: {node.NodeId}");

            var seed = run.Determinism.DrawUInt64();
            var combatResult = _combatSystem.StartCombat(
                heroId,
                enemyIds.ToList(),
                initialEnergy,
                new CombatStartOptions(
                    seed.Value,
                    run.Determinism.ContentRevision,
                    runId,
                    node.NodeId));
            if (combatResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(combatResult.Error);

            var attached = _runManager.AttachEncounter(
                runId,
                run.Sequence,
                run.Determinism.Step,
                combatResult.Value);
            if (attached.IsFailure)
            {
                _combatSystem.RemoveCombatState(combatResult.Value.CombatId);
                return Result<CombatRunEncounterResult>.Failure(attached.Error);
            }

            return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = combatResult.Value,
                RunState = attached.Value
            });
        }
    }

    public Result<CombatRunEncounterResult> GetCurrentEncounter(Guid runId)
    {
        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatRunEncounterResult>.Failure(runResult.Error);

        var encounter = runResult.Value.GetActiveEncounter();
        if (encounter == null)
            return Result<CombatRunEncounterResult>.Failure($"Run has no active encounter: {runId}");

        var restored = _combatSystem.RestoreCombatState(encounter.Combat);
        return restored.IsFailure
            ? Result<CombatRunEncounterResult>.Failure(restored.Error)
            : Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = restored.Value,
                RunState = runResult.Value
            });
    }

    public Result<CombatRunEncounterResult> GetCombatState(Guid combatId)
    {
        var runResult = _runManager.GetRunByCombat(combatId);
        if (runResult.IsFailure)
            return Result<CombatRunEncounterResult>.Failure(runResult.Error);

        var encounter = runResult.Value.GetEncounter(combatId);
        if (encounter == null)
            return Result<CombatRunEncounterResult>.Failure($"Run-owned combat not found: {combatId}");

        var restored = _combatSystem.RestoreCombatState(encounter.Combat);
        return restored.IsFailure
            ? Result<CombatRunEncounterResult>.Failure(restored.Error)
            : Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = restored.Value,
                RunState = runResult.Value
            });
    }

    public Result<CombatRunActionResult> ExecuteAction(Guid combatId, CombatActionCommand command)
    {
        if (command.RunId is not { } runId)
            return Result<CombatRunActionResult>.Failure("RunId is required for run-coordinated combat actions");

        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            return ExecuteActionLocked(combatId, runId, command);
        }
    }

    private Result<CombatRunActionResult> ExecuteActionLocked(
        Guid combatId,
        Guid runId,
        CombatActionCommand command)
    {
        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(runResult.Error);

        var run = runResult.Value;
        var encounter = run.GetActiveEncounter();
        if (encounter == null || encounter.Combat.CombatId != combatId)
            return Result<CombatRunActionResult>.Failure($"Combat is not the active encounter for run {runId}: {combatId}");
        if (!encounter.Combat.IsActive)
            return Result<CombatRunActionResult>.Failure($"Combat is not active: {combatId}");

        var restored = _combatSystem.RestoreCombatState(encounter.Combat);
        if (restored.IsFailure)
            return Result<CombatRunActionResult>.Failure(restored.Error);

        var actor = encounter.Combat.GetEntity(command.ActorId);
        if (actor == null)
            return Result<CombatRunActionResult>.Failure($"Actor not found: {command.ActorId}");

        if (!actor.IsHero || command.ActionType is ActionType.PASS or ActionType.END_TURN)
        {
            return ExecuteAndCommit(
                combatId,
                run,
                encounter.Combat,
                command,
                consumedCardId: null,
                CardConsumeDestination.None);
        }

        var cardId = ResolveCardId(command);
        if (string.IsNullOrWhiteSpace(cardId))
            return Result<CombatRunActionResult>.Failure("CardId is required for run-coordinated combat actions");

        if (!run.Deck.Hand.Contains(cardId, StringComparer.Ordinal))
            return Result<CombatRunActionResult>.Failure($"Card '{cardId}' is not in run hand");

        var actionId = ResolveActionId(command, cardId);
        var actionResult = _actionManager.GetDefinition(actionId);
        if (actionResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(actionResult.Error);

        var actionDefinition = actionResult.Value;
        var destination = ResolveDestination(actionDefinition);
        var commandWithModifiers = command with
        {
            ActionType = actionDefinition.ActionType == ActionType.BASIC_ATTACK
                ? ActionType.BASIC_ATTACK
                : ActionType.POWER,
            PowerId = actionDefinition.ActionType == ActionType.BASIC_ATTACK
                ? null
                : actionId,
            RunModifiers = ResolveRunModifiers(runId, actionDefinition.Tags)
        };
        return ExecuteAndCommit(
            combatId,
            run,
            encounter.Combat,
            commandWithModifiers,
            destination == CardConsumeDestination.None ? null : cardId,
            destination);
    }

    private Result<CombatRunActionResult> ExecuteAndCommit(
        Guid combatId,
        RunState run,
        CombatState previousCombat,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination)
    {
        var combatResult = _combatSystem.ExecuteAction(combatId, command);
        if (combatResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(combatResult.Error);

        var committed = _runManager.CommitCombatAction(
            run.RunId,
            run.Sequence,
            previousCombat,
            combatResult.Value,
            command,
            consumedCardId,
            destination);
        if (committed.IsFailure)
        {
            _combatSystem.RestoreCombatState(previousCombat);
            return Result<CombatRunActionResult>.Failure(committed.Error);
        }

        return Result<CombatRunActionResult>.Success(new CombatRunActionResult
        {
            CombatState = combatResult.Value,
            RunState = committed.Value,
            ConsumedCardId = consumedCardId,
            Destination = destination
        });
    }

    public Result<CombatRunEncounterResult> ResolveEncounter(Guid runId, Guid combatId)
    {
        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var runResult = _runManager.GetRun(runId);
            if (runResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(runResult.Error);

            var encounter = runResult.Value.GetActiveEncounter();
            if (encounter == null || encounter.Combat.CombatId != combatId)
                return Result<CombatRunEncounterResult>.Failure($"Active run encounter not found: {combatId}");
            if (encounter.Combat.IsActive)
                return Result<CombatRunEncounterResult>.Failure($"Combat is still active: {combatId}");

            var resolved = _runManager.ResolveEncounter(runId, runResult.Value.Sequence, combatId);
            if (resolved.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(resolved.Error);

            _combatSystem.RemoveCombatState(combatId);
            return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = encounter.Combat,
                RunState = resolved.Value
            });
        }
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
