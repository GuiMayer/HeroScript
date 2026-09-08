using Core.Combat.Models;
using Core.Combat.Flow;
using Core.Combat.Gambits;
using Core.Combat.Intents;
using Core.Combat.LegalActions;
using Core.Common;
using Core.Determinism;
using Core.Events;
using Core.Events.Domain;
using Core.Run;
using Core.Run.Content;
using Core.StatusEffects;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Core.Combat;

public sealed class CombatRunCoordinator : ICombatRunCoordinator
{
    private readonly ICombatFactory _combatFactory;
    private readonly IRunEncounterRuntime _runManager;
    private readonly IOperationalEventBus? _eventBus;
    private readonly ICombatFlowPlanner? _flowPlanner;
    private readonly IDecisionPolicyRegistry? _decisions;
    private readonly ILegalActionResolver? _legalActions;
    private readonly IRunCombatResolutionCommitter? _resolutionCommitter;
    private readonly ConcurrentDictionary<Guid, object> _runLocks = new();

    public CombatRunCoordinator(
        ICombatFactory combatFactory,
        IRunEncounterRuntime runManager,
        ICombatFlowPlanner? flowPlanner = null,
        IDecisionPolicyRegistry? decisions = null,
        ILegalActionResolver? legalActions = null,
        IOperationalEventBus? eventBus = null)
    {
        _combatFactory = combatFactory;
        _runManager = runManager;
        _eventBus = eventBus;
        _flowPlanner = flowPlanner;
        _decisions = decisions;
        _legalActions = legalActions;
        _resolutionCommitter = runManager;
    }

    public Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        IReadOnlyList<CombatParticipantReference> participants,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, float>>? initialResourceValues = null,
        RunCommandIdentity? commandIdentity = null,
        JsonElement commandPayload = default)
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
            if (run.ResolvedMode == null)
                return Result<CombatRunEncounterResult>.Failure(
                    "Run has no resolved game mode; canonical combat cannot start");
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
            if (node.Activity.Type != RunActivityType.Encounter)
                return Result<CombatRunEncounterResult>.Failure($"Current map node is not an encounter: {node.NodeId}");

            var seed = run.Determinism.DrawUInt64();
            var combatResult = _combatFactory.Create(
                participants,
                new CombatStartOptions(
                    seed.Value,
                    run.Determinism.ContentRevision,
                    runId,
                    node.NodeId,
                    $"run-combat:{runId:N}:{node.NodeId}",
                    InitialResourceValues: initialResourceValues));
            if (combatResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(combatResult.Error);

            var effectiveIdentity = commandIdentity ?? CreateImplicitEncounterIdentity(run, combatResult.Value);
            var initialized = InitializeCanonicalFlow(run with { Determinism = seed.Context }, combatResult.Value);
            if (initialized.IsFailure)
            {
                return Result<CombatRunEncounterResult>.Failure(initialized.Error);
            }

            var attached = _runManager.AttachEncounter(
                runId,
                effectiveIdentity.ExpectedSequence,
                effectiveIdentity.ExpectedStep,
                initialized.Value.Combat,
                effectiveIdentity,
                initialized.Value.Run,
                InitialCommand(combatResult.Value),
                combatResult.Value,
                InitializationStep(initialized.Value),
                commandPayload);
            if (attached.IsFailure)
            {
                return Result<CombatRunEncounterResult>.Failure(attached.Error);
            }

            return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = initialized.Value.Combat,
                RunState = attached.Value
            });
        }
    }

    public Result<CombatRunEncounterResult> StartEncounter(
        Guid runId,
        IReadOnlyList<CombatActorState> participants,
        RunCommandIdentity? commandIdentity = null,
        IReadOnlyDictionary<string, IReadOnlyList<StatusEffectInstance>>? initialStatusEffects = null,
        JsonElement commandPayload = default)
    {
        ArgumentNullException.ThrowIfNull(participants);
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
            if (run.ResolvedMode == null)
                return Result<CombatRunEncounterResult>.Failure(
                    "Run has no resolved game mode; canonical combat cannot start");
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
            if (node == null || node.Activity.Type != RunActivityType.Encounter)
                return Result<CombatRunEncounterResult>.Failure("Current map node is not an encounter");

            var seed = run.Determinism.DrawUInt64();
            var combatResult = _combatFactory.Create(
                participants,
                new CombatStartOptions(
                    seed.Value,
                    run.Determinism.ContentRevision,
                    runId,
                    node.NodeId,
                    $"run-combat:{runId:N}:{node.NodeId}",
                    initialStatusEffects));
            if (combatResult.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(combatResult.Error);

            var effectiveIdentity = commandIdentity ?? CreateImplicitEncounterIdentity(run, combatResult.Value);
            var initialized = InitializeCanonicalFlow(run with { Determinism = seed.Context }, combatResult.Value);
            if (initialized.IsFailure)
            {
                return Result<CombatRunEncounterResult>.Failure(initialized.Error);
            }

            var attached = _runManager.AttachEncounter(
                runId,
                effectiveIdentity.ExpectedSequence,
                effectiveIdentity.ExpectedStep,
                initialized.Value.Combat,
                effectiveIdentity,
                initialized.Value.Run,
                InitialCommand(combatResult.Value),
                combatResult.Value,
                InitializationStep(initialized.Value),
                commandPayload);
            if (attached.IsFailure)
            {
                return Result<CombatRunEncounterResult>.Failure(attached.Error);
            }

            return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
            {
                CombatState = initialized.Value.Combat,
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

        return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
        {
            CombatState = encounter.Combat,
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

        return Result<CombatRunEncounterResult>.Success(new CombatRunEncounterResult
        {
            CombatState = encounter.Combat,
            RunState = runResult.Value
        });
    }

    public Result<CombatRunActionResult> ExecuteAction(
        Guid combatId,
        CombatActionCommand command,
        RunCommandIdentity? commandIdentity = null,
        JsonElement commandPayload = default)
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

            return ExecuteActionLocked(combatId, runId, command, commandIdentity, commandPayload);
        }
    }

    private Result<CombatRunActionResult> ExecuteActionLocked(
        Guid combatId,
        Guid runId,
        CombatActionCommand command,
        RunCommandIdentity? commandIdentity,
        JsonElement commandPayload)
    {
        var runResult = _runManager.GetRun(runId);
        if (runResult.IsFailure)
            return Result<CombatRunActionResult>.Failure(runResult.Error);

        var run = runResult.Value;
        if (run.ResolvedMode == null)
            return Result<CombatRunActionResult>.Failure(
                "Run has no resolved game mode; canonical combat cannot execute commands");
        var encounter = run.GetActiveEncounter();
        if (encounter == null || encounter.Combat.CombatId != combatId)
            return Result<CombatRunActionResult>.Failure($"Combat is not the active encounter for run {runId}: {combatId}");
        if (!encounter.Combat.IsActive)
            return Result<CombatRunActionResult>.Failure($"Combat is not active: {combatId}");

        var versionValidation = ValidateRunVersion(run, commandIdentity, useCombatStep: true);
        if (versionValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(versionValidation.Error);

        if (_legalActions == null)
            return Result<CombatRunActionResult>.Failure("Canonical legal action resolver is unavailable");
        var legal = _legalActions.Evaluate(
            run,
            encounter.Combat,
            command,
            CombatCommandOrigin.PlayerInput);
        if (legal.IsFailure)
            return Result<CombatRunActionResult>.Failure(legal.Error);
        if (!legal.Value.IsLegal)
            return Result<CombatRunActionResult>.Failure(string.Join("; ", legal.Value.FailureReasons));
        var candidate = legal.Value.Candidate!;
        return ExecuteAndCommit(
            combatId,
            run,
            encounter.Combat,
            candidate,
            candidate.Command with { DeferTurnLifecycle = true },
            candidate.Command.CardInstanceId?.ToString(),
            candidate.CardPlay?.Destination ?? CardConsumeDestination.None,
            commandIdentity,
            candidate.CardPlay,
            candidate.Ability,
            commandPayload: commandPayload);
    }

    private Result<CombatRunActionResult> ExecuteAndCommit(
        Guid combatId,
        RunState run,
        CombatState previousCombat,
        LegalActionCandidate candidate,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination,
        RunCommandIdentity? commandIdentity,
        CardPlayExecutionResult? cardPlay = null,
        AbilityExecutionResult? ability = null,
        JsonElement commandPayload = default)
        => ExecuteCanonicalAndCommit(
            combatId,
            run,
            previousCombat,
            candidate,
            command,
            consumedCardId,
            destination,
            commandIdentity,
            cardPlay,
            ability,
            commandPayload);

    private Result<CombatRunActionResult> ExecuteCanonicalAndCommit(
        Guid combatId,
        RunState run,
        CombatState previousCombat,
        LegalActionCandidate candidate,
        CombatActionCommand command,
        string? consumedCardId,
        CardConsumeDestination destination,
        RunCommandIdentity? commandIdentity,
        CardPlayExecutionResult? cardPlay,
        AbilityExecutionResult? ability,
        JsonElement commandPayload)
    {
        if (_flowPlanner == null || _decisions == null || _legalActions == null || _resolutionCommitter == null)
            return Result<CombatRunActionResult>.Failure(
                "Canonical combat flow services are unavailable");

        var policies = run.ResolvedMode!.CombatRules.Flow;
        var inputValidation = CombatFlowTransitions.ValidateCommandInput(previousCombat, command);
        if (inputValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(inputValidation.Error);

        var effectiveIdentity = commandIdentity ?? CreateImplicitIdentity(run, previousCombat, command);
        var effectiveCommandType = effectiveIdentity.Type;
        var budgetValidation = CombatFlowTransitions.ValidateActionBudget(
            run,
            previousCombat,
            command,
            policies.ActionBudget,
            effectiveCommandType);
        if (budgetValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(budgetValidation.Error);

        var commandModifierIds = run.Modifiers.Select(item => item.InstanceId).ToHashSet();
        run = candidate.SuccessorRun;
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
        var executed = Result<CombatState>.Success(candidate.SuccessorCombat);
        if (executed.IsFailure)
            return Result<CombatRunActionResult>.Failure(executed.Error);

        var nextCombat = CombatFlowTransitions.ConsumeActionBudget(
            run,
            executed.Value,
            effectiveCommand,
            policies.ActionBudget,
            effectiveCommandType);
        nextCombat = CombatFlowTransitions.EvaluateOutcome(
            nextCombat,
            policies.Outcome,
            command.ActorId);

        var resolutionPayload = JsonSerializer.SerializeToElement(new
        {
            combatId,
            command = effectiveCommand,
            consumedCardId,
            destination = destination.ToString(),
            cardResolution = cardPlay == null ? null : new
            {
                cardInstanceId = cardPlay.Card.CardInstanceId,
                definitionId = cardPlay.Card.DefinitionId,
                fingerprint = cardPlay.Card.Fingerprint,
                resolutionFingerprint = cardPlay.ResolutionFingerprint,
                calculations = cardPlay.Calculations,
                applications = cardPlay.Applications,
                steps = cardPlay.Steps
            },
            abilityResolution = ability == null ? null : new
            {
                actionId = ability.Definition.ActionId,
                resolutionFingerprint = ability.ResolutionFingerprint,
                calculations = ability.Calculations,
                applications = ability.Applications,
                steps = ability.Steps
            },
            phaseTransitions = candidate.PhaseTransitions,
            candidate.ResolutionFingerprint
        });
        var rootPayload = commandPayload.ValueKind == JsonValueKind.Undefined
            ? resolutionPayload
            : commandPayload.Clone();
        run = Core.Combat.Modifiers.ModifierTransitions.Tick(run,
            Core.Combat.Modifiers.ModifierDurationBoundary.Command, previousCombat, command.ActorId, commandModifierIds);
        var steps = new List<CombatResolutionStep>
        {
            new()
            {
                TransitionType = "combat.action.applied",
                Combat = nextCombat,
                Deck = rootDeck.Value.State,
                RunDeterminism = rootDeck.Value.Context,
                RunSnapshot = run,
                EffectSteps = candidate.Steps,
                Calculations = candidate.Calculations,
                Applications = candidate.Applications,
                Payload = resolutionPayload
            }
        };
        var currentCombat = nextCombat;
        var currentDeck = rootDeck.Value.State;
        var currentRunDeterminism = rootDeck.Value.Context.AdvanceStep();
        run = run with { Deck = currentDeck, Determinism = currentRunDeterminism };
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
                return Fail<CombatRunActionResult>(advanced.Error);
            (currentCombat, currentDeck, currentRunDeterminism) = advanced.Value;
            run = LatestGameplay(run, steps, currentDeck, currentRunDeterminism);

            while (currentCombat.IsActive &&
                   currentCombat.ActivationState is { WaitingForInput: false } activation)
            {
                if (automaticSteps >= policies.AutomaticResolution.MaxAutomaticSteps)
                {
                    return Fail<CombatRunActionResult>(
                        $"Automatic resolution exceeded {policies.AutomaticResolution.MaxAutomaticSteps} steps");
                }
                if (string.IsNullOrWhiteSpace(activation.ActiveActorId))
                    return Fail<CombatRunActionResult>("Automatic activation has no actor");
                var actor = currentCombat.GetActor(activation.ActiveActorId);
                if (actor == null || currentCombat.ControllerOf(actor) != ControllerKind.AI)
                    return Fail<CombatRunActionResult>(
                        $"Automatic activation actor is invalid: {activation.ActiveActorId}");

                var lockedIntent = policies.Ai.Intent.Refresh == IntentRefreshStrategy.LockUntilActorActivation
                    ? currentCombat.ActivationState?.Intents.FirstOrDefault(intent =>
                        string.Equals(intent.ActorId, actor.InstanceId, StringComparison.Ordinal))
                    : null;
                var decision = lockedIntent == null
                    ? _decisions.Decide(actor.ControllerBinding,
                        new DecisionPolicyRequest(run, currentCombat, actor.InstanceId, policies.Ai.DecisionIds))
                    : null;
                if (decision?.IsFailure == true)
                    return Fail<CombatRunActionResult>(decision.Error);

                var aiCommand = lockedIntent?.ToCommand(run.RunId) ?? decision!.Value.Candidate.Command;
                var ruleId = lockedIntent?.RuleId ?? decision!.Value.RuleId;
                var aiAction = ExecuteAutomaticAction(
                    combatId,
                    run,
                    currentCombat,
                    currentDeck,
                    currentRunDeterminism,
                    aiCommand,
                    "combat.ai.action",
                    policies,
                    ruleId);
                if (aiAction.IsFailure && lockedIntent != null &&
                    policies.Ai.Intent.WhenInvalid is InvalidIntentStrategy.Recompute or InvalidIntentStrategy.Hide)
                {
                    decision = _decisions.Decide(actor.ControllerBinding,
                        new DecisionPolicyRequest(run, currentCombat, actor.InstanceId, policies.Ai.DecisionIds));
                    if (decision.IsFailure)
                        return Fail<CombatRunActionResult>(decision.Error);
                    aiCommand = decision.Value.Candidate.Command;
                    ruleId = decision.Value.RuleId;
                    aiAction = ExecuteAutomaticAction(
                        combatId,
                        run,
                        currentCombat,
                        currentDeck,
                        currentRunDeterminism,
                        aiCommand,
                        "combat.ai.action",
                        policies,
                        ruleId);
                }
                if (aiAction.IsFailure)
                    return Fail<CombatRunActionResult>(aiAction.Error);
                steps.Add(aiAction.Value.Step);
                automaticSteps++;
                (currentCombat, currentDeck, currentRunDeterminism) = aiAction.Value.State;
                run = LatestGameplay(run, steps, currentDeck, currentRunDeterminism);
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
                            ActorId = actor.InstanceId,
                            ActionType = ActionType.END_TURN
                        },
                        "combat.ai.end_turn",
                        policies,
                        ruleId);
                    if (endTurn.IsFailure)
                        return Fail<CombatRunActionResult>(endTurn.Error);
                    steps.Add(endTurn.Value.Step);
                    automaticSteps++;
                    (currentCombat, currentDeck, currentRunDeterminism) = endTurn.Value.State;
                    run = LatestGameplay(run, steps, currentDeck, currentRunDeterminism);
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
                        return Fail<CombatRunActionResult>(aiAdvanced.Error);
                    (currentCombat, currentDeck, currentRunDeterminism) = aiAdvanced.Value;
                    run = LatestGameplay(run, steps, currentDeck, currentRunDeterminism);
                }
            }
        }

        if (!currentCombat.IsActive && !currentCombat.CompletedLifecycleBoundaries.Contains(CombatTriggerBoundaries.CombatEnd))
        {
            var completed = _flowPlanner!.Complete(run with { Deck = currentDeck, Determinism = currentRunDeterminism }, currentCombat);
            if (completed.IsFailure) return Fail<CombatRunActionResult>(completed.Error);
            currentCombat = completed.Value.Combat;
            run = completed.Value.Run ?? run;
            currentDeck = run.Deck;
            currentRunDeterminism = run.Determinism;
            steps.Add(new()
            {
                TransitionType = "combat.completed", Combat = currentCombat, Deck = currentDeck,
                RunDeterminism = currentRunDeterminism,
                RunSnapshot = run,
                EffectSteps = completed.Value.Events.SelectMany(item => item.Steps).ToArray(),
                Calculations = completed.Value.Events.SelectMany(item => item.Steps)
                    .Where(item => item.Calculation != null)
                    .Select(item => item.Calculation!)
                    .ToArray(),
                Applications = completed.Value.Events.SelectMany(item => item.Applications).ToArray(),
                Payload = JsonSerializer.SerializeToElement(new { events = completed.Value.Events })
            });
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
            return Fail<CombatRunActionResult>(committed.Error);

        if (cardPlay != null)
            PublishCardAction(cardPlay);
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
        return Result<(CombatState, DeckState, DeterministicContext)>.Success(
            (plan.Value.Combat, plan.Value.Deck, determinism));
    }

    private static RunState LatestGameplay(RunState run, IEnumerable<CombatResolutionStep> steps,
        DeckState deck, DeterministicContext context) =>
        (steps.LastOrDefault(step => step.RunSnapshot != null)?.RunSnapshot ?? run) with { Deck = deck, Determinism = context };

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
            string? decisionRuleId)
    {
        if (_legalActions == null)
            return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Failure(
                "Canonical legal action resolver is unavailable");
        run = run with { Deck = deck, Determinism = determinism };
        var legal = _legalActions.Evaluate(run, combat, command, CombatCommandOrigin.AutomaticController);
        if (legal.IsFailure)
            return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Failure(legal.Error);
        if (!legal.Value.IsLegal)
            return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Failure(
                string.Join("; ", legal.Value.FailureReasons));
        var candidate = legal.Value.Candidate!;
        command = candidate.Command with
        {
            IgnoreConfiguredCosts = policies.ActionBudget.ActionCosts == ActionCostStrategy.Ignore,
            DeferTurnLifecycle = true
        };
        var ability = candidate.Ability;
        var executed = Result<CombatState>.Success(candidate.SuccessorCombat);
        var next = CombatFlowTransitions.ConsumeActionBudget(
            run,
            executed.Value,
            command,
            policies.ActionBudget,
            "EXECUTE_ACTION");
        next = CombatFlowTransitions.EvaluateOutcome(next, policies.Outcome, command.ActorId);
        var modifierIds = run.Modifiers.Select(item => item.InstanceId).ToHashSet();
        run = candidate.SuccessorRun;
        run = Core.Combat.Modifiers.ModifierTransitions.Tick(run,
            Core.Combat.Modifiers.ModifierDurationBoundary.Command, combat, command.ActorId, modifierIds);
        deck = run.Deck;
        determinism = run.Determinism;
        var step = new CombatResolutionStep
        {
            TransitionType = transitionType,
            Combat = next,
            Deck = deck,
            RunDeterminism = determinism,
            RunSnapshot = run,
            EffectSteps = candidate.Steps,
            Calculations = candidate.Calculations,
            Applications = candidate.Applications,
            Payload = JsonSerializer.SerializeToElement(new
            {
                command,
                decisionRuleId,
                phaseTransitions = candidate.PhaseTransitions,
                candidate.ResolutionFingerprint,
                abilityResolution = ability == null ? null : new
                {
                    ability.Definition.ActionId,
                    ability.ResolutionFingerprint,
                    ability.Calculations,
                    ability.Applications
                }
            })
        };
        return Result<(CombatResolutionStep, (CombatState, DeckState, DeterministicContext))>.Success(
            (step, (next, deck, determinism.AdvanceStep())));
    }

    private Result<CombatInitializationResult> InitializeCanonicalFlow(RunState run, CombatState combat)
    {
        if (run.ResolvedMode == null)
            return Result<CombatInitializationResult>.Failure(
                "Run has no resolved game mode; canonical combat cannot initialize");
        if (_flowPlanner == null) return Result<CombatInitializationResult>.Failure("Canonical combat flow planner is unavailable");
        var initialized = _flowPlanner.InitializeTransaction(run, combat);
        if (initialized.IsFailure) return initialized;
        return initialized;
    }

    private static RunEncounterStartCommand InitialCommand(CombatState combat) => new(combat.GetAllActors().ToArray(),
        combat.StatusEffects.ToDictionary(item => item.Key, item => (IReadOnlyList<StatusEffectInstance>)item.Value.ToArray(),
            StringComparer.Ordinal));

    private static CombatResolutionStep InitializationStep(CombatInitializationResult initialized) => new()
    {
        TransitionType = "combat.initialized",
        Combat = initialized.Combat,
        Deck = initialized.Run.Deck,
        RunSnapshot = initialized.Run,
        RunDeterminism = initialized.Run.Determinism,
        EffectSteps = initialized.EffectSteps,
        Calculations = initialized.Calculations,
        Applications = initialized.Applications,
        Payload = JsonSerializer.SerializeToElement(new
        {
            activeActorId = initialized.Combat.ActivationState?.ActiveActorId,
            phaseId = initialized.Combat.PhaseState?.Cursor,
            phaseTransitions = initialized.PhaseTransitions,
            initializationFingerprint = initialized.Fingerprint
        })
    };

    private static RunCommandIdentity CreateImplicitEncounterIdentity(RunState run, CombatState combat) => new(
        DeterministicId.Create(
            run.Determinism.Seed,
            (ulong)run.Sequence,
            $"implicit-start-encounter:{combat.RunNodeId}:{combat.CombatId:N}"),
        RunCommandTypes.StartEncounter,
        run.Sequence,
        run.Determinism.Step);

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
            combat.Determinism.Step);

    private static Result<T> Fail<T>(string error) => Result<T>.Failure(error);

    public Result<CombatRunEncounterResult> ResolveEncounter(
        Guid runId,
        Guid combatId,
        RunCommandIdentity? commandIdentity = null,
        JsonElement commandPayload = default)
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
                commandIdentity,
                commandPayload);
            if (resolved.IsFailure)
                return Result<CombatRunEncounterResult>.Failure(resolved.Error);

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
        if (identity == null)
            return Result<CombatRunEncounterResult?>.Success(null);

        var receipt = _runManager.FindReceipt(runId, identity.CommandId);
        if (receipt == null)
            return Result<CombatRunEncounterResult?>.Success(null);
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
        if (identity == null)
            return Result<CombatRunActionResult?>.Success(null);

        var receipt = _runManager.FindReceipt(runId, identity.CommandId);
        if (receipt == null)
            return Result<CombatRunActionResult?>.Success(null);
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

    private void PublishCardAction(CardPlayExecutionResult result)
    {
        if (_eventBus == null)
            return;

        var action = result.Combat.ActionHistory.Last();
        _eventBus.Publish(new ActionExecutedEvent
        {
            CombatId = result.Combat.CombatId,
            ActionId = action.ActionId,
            ActorId = action.ActorId,
            ActionTypeName = ActionType.PLAY_CARD.ToString(),
            TargetId = action.TargetId,
            Applications = action.Applications,
            Turn = action.Turn,
            Subject = action.ActorId,
            Target = action.TargetId ?? "none",
            Payload = new Dictionary<string, object>
            {
                ["cardInstanceId"] = result.Card.CardInstanceId,
                ["cardDefinitionId"] = result.Card.DefinitionId,
                ["resolutionFingerprint"] = result.ResolutionFingerprint
            }
        });

        foreach (var application in action.Applications)
        {
            if (application.ResourceId is not { } resourceId ||
                application.ResourceField is not { } field ||
                application.PreviousValue is not { } previousValue ||
                application.CurrentValue is not { } currentValue)
                continue;
            _eventBus.Publish(new ResourceChangedEvent
            {
                CombatId = result.Combat.CombatId,
                ActionId = action.ActionId,
                OwnerId = application.TargetEntityId,
                ResourceId = resourceId,
                Field = field,
                PreviousValue = previousValue,
                CurrentValue = currentValue,
                Reason = $"Card: {result.Card.DefinitionId}",
                Turn = action.Turn,
                Subject = application.TargetEntityId,
                Target = resourceId
            });
        }
    }

}
