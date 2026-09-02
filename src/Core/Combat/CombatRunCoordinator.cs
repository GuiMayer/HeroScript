using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.StatusEffects;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Core.Combat;

public sealed class CombatRunCoordinator : ICombatRunCoordinator
{
    private const string BasicAttackActionId = "basic_attack";

    private readonly ICombatSystem _combatSystem;
    private readonly IRunManager _runManager;
    private readonly IActionManager _actionManager;
    private readonly IScriptModifierManager? _scriptModifierManager;
    private readonly ICombatFlowPlanner? _flowPlanner;
    private readonly IGambitEngine? _gambitEngine;
    private readonly IRunCombatResolutionCommitter? _resolutionCommitter;
    private readonly ConcurrentDictionary<Guid, object> _runLocks = new();

    public CombatRunCoordinator(
        ICombatSystem combatSystem,
        IRunManager runManager,
        IActionManager actionManager,
        IScriptModifierManager? scriptModifierManager = null,
        ICombatFlowPlanner? flowPlanner = null,
        IGambitEngine? gambitEngine = null)
    {
        _combatSystem = combatSystem;
        _runManager = runManager;
        _actionManager = actionManager;
        _scriptModifierManager = scriptModifierManager;
        _flowPlanner = flowPlanner;
        _gambitEngine = gambitEngine;
        _resolutionCommitter = runManager as IRunCombatResolutionCommitter;
    }

    public Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        string heroId,
        IReadOnlyList<string> enemyIds,
        int initialEnergy = 3,
        RunCommandIdentity? commandIdentity = null)
    {
        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var duplicate = FindDuplicateEncounter(runId, commandIdentity);
            if (duplicate.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(duplicate.Error);
            if (duplicate.Value != null)
                return Result<CombatRunEncounterResult>.Success(duplicate.Value);

            var runResult = _runManager.GetRun(runId);
            if (runResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(runResult.Error);

            var run = runResult.Value;
            var versionValidation = ValidateRunVersion(run, commandIdentity, useCombatStep: false);
            if (versionValidation.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(versionValidation.Error);
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
                    node.NodeId,
                    $"run-combat:{runId:N}:{node.NodeId}"));
            if (combatResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(combatResult.Error);

            var initialized = InitializeCanonicalFlow(run, combatResult.Value);
            if (initialized.IsFailure)
            {
                _combatSystem.RemoveCombatState(combatResult.Value.CombatId);
                return Result<CombatRunEncounterResult>.Failure(initialized.Error);
            }

            var attached = _runManager.AttachEncounter(
                runId,
                commandIdentity?.ExpectedSequence ?? run.Sequence,
                commandIdentity?.ExpectedStep ?? run.Determinism.Step,
                initialized.Value,
                commandIdentity);
            if (attached.IsFailure)
            {
                _combatSystem.RemoveCombatState(combatResult.Value.CombatId);
                return Result<CombatRunEncounterResult>.Failure(attached.Error);
            }

            return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = initialized.Value,
                RunState = attached.Value
            });
        }
    }

    public Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        CombatEntity hero,
        IReadOnlyList<CombatEntity> enemies,
        RunCommandIdentity? commandIdentity = null,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatusEffects = null)
    {
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(enemies);
        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var duplicate = FindDuplicateEncounter(runId, commandIdentity);
            if (duplicate.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(duplicate.Error);
            if (duplicate.Value != null)
                return Result<CombatRunEncounterResult>.Success(duplicate.Value);

            var runResult = _runManager.GetRun(runId);
            if (runResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(runResult.Error);
            var run = runResult.Value;
            var versionValidation = ValidateRunVersion(run, commandIdentity, useCombatStep: false);
            if (versionValidation.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(versionValidation.Error);
            if (run.GetActiveEncounter() != null)
                return Result<CombatRunEncounterResult>.Failure(
                    $"Run already has an active encounter: {run.ActiveEncounterId}");
            if (run.CurrentNodeId == null)
                return Result<CombatRunEncounterResult>.Failure("Run has no current map node");

            var node = run.Map.Nodes.FirstOrDefault(item =>
                string.Equals(item.NodeId, run.CurrentNodeId, StringComparison.Ordinal));
            if (node == null || !RunMapTransitions.IsEncounterNode(node.NodeType))
                return Result<CombatRunEncounterResult>.Failure("Current map node is not an encounter");

            var seed = run.Determinism.DrawUInt64();
            var combatResult = _combatSystem.StartCombatWithCombatEntities(
                hero,
                enemies,
                new CombatStartOptions(
                    seed.Value,
                    run.Determinism.ContentRevision,
                    runId,
                    node.NodeId,
                    $"run-combat:{runId:N}:{node.NodeId}",
                    initialStatusEffects));
            if (combatResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(combatResult.Error);

            var initialized = InitializeCanonicalFlow(run, combatResult.Value);
            if (initialized.IsFailure)
            {
                _combatSystem.RemoveCombatState(combatResult.Value.CombatId);
                return Result<CombatRunEncounterResult>.Failure(initialized.Error);
            }

            var attached = _runManager.AttachEncounter(
                runId,
                commandIdentity?.ExpectedSequence ?? run.Sequence,
                commandIdentity?.ExpectedStep ?? run.Determinism.Step,
                initialized.Value,
                commandIdentity);
            if (attached.IsFailure)
            {
                _combatSystem.RemoveCombatState(combatResult.Value.CombatId);
                return Result<CombatRunEncounterResult>.Failure(attached.Error);
            }

            return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = initialized.Value,
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

    public Result<CombatRunActionResult> ExecuteAction(
        Guid combatId,
        CombatActionCommand command,
        RunCommandIdentity? commandIdentity = null)
    {
        if (command.RunId is not { } runId)
            return Result<CombatRunActionResult>.Failure("RunId is required for run-coordinated combat actions");

        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var duplicate = FindDuplicateAction(runId, combatId, commandIdentity);
            if (duplicate.IsFailure)
                return Result<CombatRunActionResult>.Failure(duplicate.Error);
            if (duplicate.Value != null)
                return Result<CombatRunActionResult>.Success(duplicate.Value);

            return ExecuteActionLocked(combatId, runId, command, commandIdentity);
        }
    }

    private Result<CombatRunActionResult> ExecuteActionLocked(
        Guid combatId,
        Guid runId,
        CombatActionCommand command,
        RunCommandIdentity? commandIdentity)
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

        var versionValidation = ValidateRunVersion(run, commandIdentity, useCombatStep: true);
        if (versionValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(versionValidation.Error);

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
                CardConsumeDestination.None,
                commandIdentity);
        }

        var cardId = ResolveCardId(command);
        if (string.IsNullOrWhiteSpace(cardId))
            return Result<CombatRunActionResult>.Failure("CardId is required for run-coordinated combat actions");

        if (!IsCardInHand(run.Deck, cardId))
            return Result<CombatRunActionResult>.Failure($"Card '{cardId}' is not in run hand");

        var actionId = ResolveActionId(command, cardId);
        var actionResult = _actionManager is IRevisionedActionCatalog revisionedActions
            ? revisionedActions.GetDefinition(actionId, run.Determinism.ContentRevision, run.ConfigName)
            : _actionManager.GetDefinition(actionId);
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
            destination,
            commandIdentity);
    }

    private Result<CombatRunActionResult> ExecuteAndCommit(
        Guid combatId,
        RunState run,
        CombatState previousCombat,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination,
        RunCommandIdentity? commandIdentity)
    {
        if (run.ResolvedMode != null)
            return ExecuteCanonicalAndCommit(
                combatId,
                run,
                previousCombat,
                command,
                consumedCardId,
                destination,
                commandIdentity);

        var actionBudget = run.ResolvedMode?.CombatRules.Flow.ActionBudget;
        var effectiveCommandType = commandIdentity?.Type ?? "COMBAT_ACTION";
        if (actionBudget != null)
        {
            var budgetValidation = CombatFlowTransitions.ValidateActionBudget(
                previousCombat,
                command,
                actionBudget,
                effectiveCommandType);
            if (budgetValidation.IsFailure)
                return Result<CombatRunActionResult>.Failure(budgetValidation.Error);
        }

        var effectiveCommand = commandIdentity == null
            ? command
            : command with { ExpectedStep = commandIdentity.ExpectedStep };
        var combatResult = _combatSystem.ExecuteAction(combatId, effectiveCommand);
        if (combatResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(combatResult.Error);
        var nextCombat = actionBudget == null
            ? combatResult.Value
            : CombatFlowTransitions.ConsumeActionBudget(
                combatResult.Value,
                effectiveCommand,
                actionBudget,
                effectiveCommandType);

        var committed = _runManager.CommitCombatAction(
            run.RunId,
            run.Sequence,
            previousCombat,
            nextCombat,
            effectiveCommand,
            consumedCardId,
            destination,
            commandIdentity);
        if (committed.IsFailure)
        {
            _combatSystem.RestoreCombatState(previousCombat);
            return Result<CombatRunActionResult>.Failure(committed.Error);
        }

        return Result<CombatRunActionResult>.Success(new CombatRunActionResult
        {
            CombatState = nextCombat,
            RunState = committed.Value,
            ConsumedCardId = consumedCardId,
            Destination = destination
        });
    }

    private Result<CombatRunActionResult> ExecuteCanonicalAndCommit(
        Guid combatId,
        RunState run,
        CombatState previousCombat,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination,
        RunCommandIdentity? commandIdentity)
    {
        if (_flowPlanner == null || _gambitEngine == null || _resolutionCommitter == null)
            return Result<CombatRunActionResult>.Failure(
                "Canonical combat flow services are unavailable");

        var policies = run.ResolvedMode!.CombatRules.Flow;
        var inputValidation = ValidateCanonicalInput(previousCombat, command);
        if (inputValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(inputValidation.Error);

        var effectiveIdentity = commandIdentity ?? CreateImplicitIdentity(run, previousCombat, command);
        var effectiveCommandType = effectiveIdentity.Type;
        var budgetValidation = CombatFlowTransitions.ValidateActionBudget(
            previousCombat,
            command,
            policies.ActionBudget,
            effectiveCommandType);
        if (budgetValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(budgetValidation.Error);

        var rootDeck = Result<DeckTransition>.Success(
            new DeckTransition(run.Deck, run.Determinism, []));
        if (!string.IsNullOrWhiteSpace(consumedCardId) && destination != CardConsumeDestination.None)
        {
            rootDeck = DeckTransitions.MoveFromHand(
                run.Deck,
                [consumedCardId],
                destination,
                run.Determinism);
            if (rootDeck.IsFailure)
                return Result<CombatRunActionResult>.Failure(rootDeck.Error);
        }

        var effectiveCommand = command with
        {
            ExpectedStep = effectiveIdentity.ExpectedStep,
            IgnoreConfiguredCosts = policies.ActionBudget.ActionCosts == ActionCostStrategy.Ignore,
            DeferTurnLifecycle = true
        };
        var executed = _combatSystem.ExecuteAction(combatId, effectiveCommand);
        if (executed.IsFailure)
            return Result<CombatRunActionResult>.Failure(executed.Error);

        var nextCombat = CombatFlowTransitions.ConsumeActionBudget(
            executed.Value,
            effectiveCommand,
            policies.ActionBudget,
            effectiveCommandType);
        nextCombat = CombatFlowTransitions.EvaluateOutcome(
            nextCombat,
            policies.Outcome,
            command.ActorId);

        var rootPayload = JsonSerializer.SerializeToElement(new
        {
            combatId,
            command = effectiveCommand,
            consumedCardId,
            destination = destination.ToString()
        });
        var steps = new List<CombatResolutionStep>
        {
            new()
            {
                TransitionType = "combat.action.applied",
                Combat = nextCombat,
                Deck = rootDeck.Value.State,
                RunDeterminism = rootDeck.Value.Context,
                Payload = rootPayload
            }
        };
        var currentCombat = nextCombat;
        var currentDeck = rootDeck.Value.State;
        var currentRunDeterminism = rootDeck.Value.Context.AdvanceStep();
        var automaticSteps = 0;

        var activationBudgetExhausted =
            policies.ActionBudget.Strategy == ActionBudgetStrategy.FixedCount &&
            currentCombat.ActivationState is { } resolvedActivation &&
            resolvedActivation.ActionsTaken >= policies.ActionBudget.MaxActionsPerActivation;
        if ((command.ActionType == ActionType.END_TURN || activationBudgetExhausted) && currentCombat.IsActive)
        {
            var advanced = AppendActivationPlan(
                run,
                currentCombat,
                currentDeck,
                currentRunDeterminism,
                steps,
                ref automaticSteps,
                policies.AutomaticResolution.MaxAutomaticSteps);
            if (advanced.IsFailure)
                return RestoreAndFail<CombatRunActionResult>(previousCombat, advanced.Error);
            (currentCombat, currentDeck, currentRunDeterminism) = advanced.Value;

            while (currentCombat.IsActive &&
                   currentCombat.ActivationState is { WaitingForInput: false } activation)
            {
                if (automaticSteps >= policies.AutomaticResolution.MaxAutomaticSteps)
                {
                    return RestoreAndFail<CombatRunActionResult>(
                        previousCombat,
                        $"Automatic resolution exceeded {policies.AutomaticResolution.MaxAutomaticSteps} steps");
                }
                if (string.IsNullOrWhiteSpace(activation.ActiveActorId))
                    return RestoreAndFail<CombatRunActionResult>(previousCombat, "Automatic activation has no actor");
                var actor = currentCombat.GetEntity(activation.ActiveActorId);
                if (actor == null || actor.IsHero)
                    return RestoreAndFail<CombatRunActionResult>(
                        previousCombat,
                        $"Automatic activation actor is invalid: {activation.ActiveActorId}");

                var decision = _gambitEngine.DecideActionWithMetadata(
                    new Entity.Entity { EntityId = actor.EntityId, DisplayName = actor.Name },
                    currentCombat,
                    policies.Ai.GambitIds.Count == 0 ? null : policies.Ai.GambitIds);
                if (decision.IsFailure)
                    return RestoreAndFail<CombatRunActionResult>(previousCombat, decision.Error);

                var aiCommand = new CombatActionCommand
                {
                    RunId = run.RunId,
                    ActorId = actor.EntityId,
                    ActionType = decision.Value.Action.ActionType,
                    PowerId = decision.Value.Action.PowerId,
                    TargetId = decision.Value.Action.TargetId,
                    CostOptionId = decision.Value.Action.CostOptionId?.ToString()
                };
                var aiAction = ExecuteAutomaticAction(
                    combatId,
                    run,
                    currentCombat,
                    currentDeck,
                    currentRunDeterminism,
                    aiCommand,
                    "combat.ai.action",
                    policies,
                    decision.Value.GambitId);
                if (aiAction.IsFailure)
                    return RestoreAndFail<CombatRunActionResult>(previousCombat, aiAction.Error);
                steps.Add(aiAction.Value.Step);
                automaticSteps++;
                (currentCombat, currentDeck, currentRunDeterminism) = aiAction.Value.State;
                if (!currentCombat.IsActive)
                    break;

                if (aiCommand.ActionType != ActionType.END_TURN && policies.Ai.AutoEndAfterAction)
                {
                    var endTurn = ExecuteAutomaticAction(
                        combatId,
                        run,
                        currentCombat,
                        currentDeck,
                        currentRunDeterminism,
                        new CombatActionCommand
                        {
                            RunId = run.RunId,
                            ActorId = actor.EntityId,
                            ActionType = ActionType.END_TURN
                        },
                        "combat.ai.end_turn",
                        policies,
                        decision.Value.GambitId);
                    if (endTurn.IsFailure)
                        return RestoreAndFail<CombatRunActionResult>(previousCombat, endTurn.Error);
                    steps.Add(endTurn.Value.Step);
                    automaticSteps++;
                    (currentCombat, currentDeck, currentRunDeterminism) = endTurn.Value.State;
                }

                if (currentCombat.IsActive &&
                    (aiCommand.ActionType == ActionType.END_TURN || policies.Ai.AutoEndAfterAction))
                {
                    var aiAdvanced = AppendActivationPlan(
                        run,
                        currentCombat,
                        currentDeck,
                        currentRunDeterminism,
                        steps,
                        ref automaticSteps,
                        policies.AutomaticResolution.MaxAutomaticSteps);
                    if (aiAdvanced.IsFailure)
                        return RestoreAndFail<CombatRunActionResult>(previousCombat, aiAdvanced.Error);
                    (currentCombat, currentDeck, currentRunDeterminism) = aiAdvanced.Value;
                }
            }
        }

        var committed = _resolutionCommitter.CommitCombatResolution(new CombatResolutionCommit
        {
            RunId = run.RunId,
            ExpectedSequence = run.Sequence,
            PreviousCombat = previousCombat,
            RootCommand = effectiveIdentity,
            RootPayload = rootPayload,
            Steps = steps
        });
        if (committed.IsFailure)
            return RestoreAndFail<CombatRunActionResult>(previousCombat, committed.Error);

        var restored = _combatSystem.RestoreCombatState(currentCombat);
        if (restored.IsFailure)
            return Result<CombatRunActionResult>.Failure(restored.Error);
        return Result<CombatRunActionResult>.Success(new CombatRunActionResult
        {
            CombatState = currentCombat,
            RunState = committed.Value,
            ConsumedCardId = consumedCardId,
            Destination = destination
        });
    }

    private Result<(CombatState Combat, DeckState Deck, DeterministicContext Determinism)> AppendActivationPlan(
        RunState run,
        CombatState combat,
        DeckState deck,
        DeterministicContext determinism,
        ICollection<CombatResolutionStep> steps,
        ref int automaticSteps,
        int maximumSteps)
    {
        var plan = _flowPlanner!.AdvanceActivation(run, combat, deck, determinism);
        if (plan.IsFailure)
            return Result<(CombatState, DeckState, DeterministicContext)>.Failure(plan.Error);
        if (automaticSteps + plan.Value.Steps.Count > maximumSteps)
            return Result<(CombatState, DeckState, DeterministicContext)>.Failure(
                $"Automatic resolution exceeded {maximumSteps} steps");
        foreach (var step in plan.Value.Steps)
        {
            steps.Add(step);
            determinism = (step.RunDeterminism ?? determinism).AdvanceStep();
        }
        automaticSteps += plan.Value.Steps.Count;
        var restored = _combatSystem.RestoreCombatState(plan.Value.Combat);
        return restored.IsFailure
            ? Result<(CombatState, DeckState, DeterministicContext)>.Failure(restored.Error)
            : Result<(CombatState, DeckState, DeterministicContext)>.Success(
                (plan.Value.Combat, plan.Value.Deck, determinism));
    }

    private Result<(CombatResolutionStep Step, (CombatState Combat, DeckState Deck, DeterministicContext Determinism) State)>
        ExecuteAutomaticAction(
            Guid combatId,
            RunState run,
            CombatState combat,
            DeckState deck,
            DeterministicContext determinism,
            CombatActionCommand command,
            string transitionType,
            CombatFlowPoliciesDefinition policies,
            string? gambitId)
    {
        var budget = CombatFlowTransitions.ValidateActionBudget(
            combat,
            command,
            policies.ActionBudget,
            "EXECUTE_ACTION");
        if (budget.IsFailure)
            return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Failure(budget.Error);
        var restored = _combatSystem.RestoreCombatState(combat);
        if (restored.IsFailure)
            return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Failure(restored.Error);
        command = command with
        {
            IgnoreConfiguredCosts = policies.ActionBudget.ActionCosts == ActionCostStrategy.Ignore,
            DeferTurnLifecycle = true
        };
        var executed = _combatSystem.ExecuteAction(combatId, command);
        if (executed.IsFailure)
            return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Failure(executed.Error);
        var next = CombatFlowTransitions.ConsumeActionBudget(
            executed.Value,
            command,
            policies.ActionBudget,
            "EXECUTE_ACTION");
        next = CombatFlowTransitions.EvaluateOutcome(next, policies.Outcome, command.ActorId);
        var step = new CombatResolutionStep
        {
            TransitionType = transitionType,
            Combat = next,
            Deck = deck,
            RunDeterminism = determinism,
            Payload = JsonSerializer.SerializeToElement(new { command, gambitId })
        };
        return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Success(
            (step, (next, deck, determinism.AdvanceStep())));
    }

    private Result<CombatState> InitializeCanonicalFlow(RunState run, CombatState combat)
    {
        if (run.ResolvedMode == null)
            return Result<CombatState>.Success(combat);
        if (_flowPlanner == null)
            return Result<CombatState>.Failure("Canonical combat flow planner is unavailable");
        var initialized = _flowPlanner.Initialize(run, combat);
        if (initialized.IsFailure)
            return initialized;
        return _combatSystem.RestoreCombatState(initialized.Value);
    }

    private static Result ValidateCanonicalInput(CombatState combat, CombatActionCommand command)
    {
        var activation = combat.ActivationState;
        if (activation == null)
            return Result.Failure("Combat activation has not been initialized");
        if (!activation.WaitingForInput)
            return Result.Failure("Combat is resolving automatic actions");
        if (!string.Equals(activation.ActiveActorId, command.ActorId, StringComparison.Ordinal))
            return Result.Failure($"Actor '{command.ActorId}' is not the active actor");
        var phase = combat.PhaseState?.PhaseSequence.Find(combat.PhaseState.CurrentPhaseId);
        if (phase == null)
            return Result.Failure("Combat phase has not been initialized");
        return phase.AllowedActions.Contains(command.ActionType)
            ? Result.Success()
            : Result.Failure(
                $"Action '{command.ActionType}' is not allowed in phase '{phase.PhaseId}'");
    }

    private static RunCommandIdentity CreateImplicitIdentity(
        RunState run,
        CombatState combat,
        CombatActionCommand command) => new(
            DeterministicId.Create(
                run.Determinism.Seed,
                (ulong)run.Sequence,
                $"implicit-combat-command:{combat.CombatId:N}"),
            "COMBAT_ACTION",
            run.Sequence,
            combat.Determinism.Step,
            CanonicalJson.ComputeHash(command));

    private Result<T> RestoreAndFail<T>(CombatState previous, string error)
    {
        _combatSystem.RestoreCombatState(previous);
        return Result<T>.Failure(error);
    }

    public Result<CombatRunEncounterResult> ResolveEncounter(
        Guid runId,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null)
    {
        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var duplicate = FindDuplicateEncounter(runId, commandIdentity, combatId);
            if (duplicate.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(duplicate.Error);
            if (duplicate.Value != null)
                return Result<CombatRunEncounterResult>.Success(duplicate.Value);

            var runResult = _runManager.GetRun(runId);
            if (runResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(runResult.Error);

            var encounter = runResult.Value.GetActiveEncounter();
            if (encounter == null || encounter.Combat.CombatId != combatId)
                return Result<CombatRunEncounterResult>.Failure($"Active run encounter not found: {combatId}");
            if (encounter.Combat.IsActive)
                return Result<CombatRunEncounterResult>.Failure($"Combat is still active: {combatId}");
            var resolutionStrategy = runResult.Value.ResolvedMode?.CombatRules.Flow.EncounterResolution.Strategy;
            if (resolutionStrategy is not null and not EncounterResolutionStrategy.ManualAck)
            {
                return Result<CombatRunEncounterResult>.Failure(
                    $"Encounter resolution strategy is not executable: {resolutionStrategy}");
            }

            var versionValidation = ValidateRunVersion(runResult.Value, commandIdentity, useCombatStep: true);
            if (versionValidation.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(versionValidation.Error);

            var resolved = _runManager.ResolveEncounter(
                runId,
                commandIdentity?.ExpectedSequence ?? runResult.Value.Sequence,
                combatId,
                commandIdentity);
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

    private Result<CombatRunEncounterResult?> FindDuplicateEncounter(
        Guid runId,
        RunCommandIdentity? identity,
        Guid? expectedCombatId = null)
    {
        if (identity == null || _runManager is not IRunCommandProcessor processor)
            return Result<CombatRunEncounterResult?>.Success(null);

        var receipt = processor.FindReceipt(runId, identity.CommandId);
        if (receipt.IsFailure)
            return Result<CombatRunEncounterResult?>.Failure(receipt.Error);
        if (receipt.Value == null)
            return Result<CombatRunEncounterResult?>.Success(null);
        if (!string.Equals(receipt.Value.CommandType, identity.Type, StringComparison.Ordinal))
            return Result<CombatRunEncounterResult?>.Failure(
                $"Command id {identity.CommandId} was already used by {receipt.Value.CommandType}");
        if (!MatchesEnvelope(receipt.Value.JournalEntry, identity))
            return Result<CombatRunEncounterResult?>.Failure(
                $"Command id {identity.CommandId} was already used with a different envelope");

        var encounter = expectedCombatId.HasValue
            ? receipt.Value.State.GetEncounter(expectedCombatId.Value)
            : receipt.Value.State.Encounters.LastOrDefault();
        return encounter == null
            ? Result<CombatRunEncounterResult?>.Failure(
                $"Command receipt {identity.CommandId} does not contain an encounter")
            : Result<CombatRunEncounterResult?>.Success(new CombatRunEncounterResult
            {
                CombatState = encounter.Combat,
                RunState = receipt.Value.State
            });
    }

    private Result<CombatRunActionResult?> FindDuplicateAction(
        Guid runId,
        Guid combatId,
        RunCommandIdentity? identity)
    {
        if (identity == null || _runManager is not IRunCommandProcessor processor)
            return Result<CombatRunActionResult?>.Success(null);

        var receipt = processor.FindReceipt(runId, identity.CommandId);
        if (receipt.IsFailure)
            return Result<CombatRunActionResult?>.Failure(receipt.Error);
        if (receipt.Value == null)
            return Result<CombatRunActionResult?>.Success(null);
        if (!string.Equals(receipt.Value.CommandType, identity.Type, StringComparison.Ordinal))
            return Result<CombatRunActionResult?>.Failure(
                $"Command id {identity.CommandId} was already used by {receipt.Value.CommandType}");
        if (!MatchesEnvelope(receipt.Value.JournalEntry, identity))
            return Result<CombatRunActionResult?>.Failure(
                $"Command id {identity.CommandId} was already used with a different envelope");

        var encounter = receipt.Value.State.GetEncounter(combatId);
        return encounter == null
            ? Result<CombatRunActionResult?>.Failure(
                $"Command receipt {identity.CommandId} does not contain combat {combatId}")
            : Result<CombatRunActionResult?>.Success(new CombatRunActionResult
            {
                CombatState = encounter.Combat,
                RunState = receipt.Value.State
            });
    }

    private static Result ValidateRunVersion(
        RunState run,
        RunCommandIdentity? identity,
        bool useCombatStep)
    {
        if (identity == null)
            return Result.Success();

        var currentStep = useCombatStep
            ? run.GetActiveEncounter()?.Combat.Determinism.Step ?? run.Determinism.Step
            : run.Determinism.Step;
        return run.Sequence == identity.ExpectedSequence && currentStep == identity.ExpectedStep
            ? Result.Success()
            : Result.Failure(
                $"{RunCommandErrors.VersionConflictPrefix} Expected sequence/step " +
                $"{identity.ExpectedSequence}/{identity.ExpectedStep}, current is {run.Sequence}/{currentStep}");
    }

    private static bool MatchesEnvelope(
        Core.Abstractions.Persistence.RunJournalEntry entry,
        RunCommandIdentity identity)
    {
        return entry.ExpectedSequence == identity.ExpectedSequence &&
               entry.ExpectedStep == identity.ExpectedStep &&
               (string.IsNullOrWhiteSpace(entry.CommandPayloadHash) ||
                string.Equals(entry.CommandPayloadHash, identity.PayloadHash, StringComparison.Ordinal));
    }

    private static string ResolveCardId(CombatActionCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.CardId))
            return command.CardId;

        return command.ActionType == ActionType.BASIC_ATTACK
            ? BasicAttackActionId
            : command.PowerId ?? string.Empty;
    }

    private static bool IsCardInHand(DeckState deck, string cardReference)
    {
        if (deck.Hand.Contains(cardReference, StringComparer.Ordinal))
            return true;
        return deck.InstanceTrackingEnabled && Guid.TryParse(cardReference, out var instanceId) &&
               deck.HandInstanceIds.Contains(instanceId);
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
