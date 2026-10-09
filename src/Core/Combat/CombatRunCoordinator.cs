using Core.Combat.Models;
using Core.Combat.Flow;
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
    private readonly ICombatFlowPlanner _flowPlanner;
    private readonly ICombatCommandHandler _commands;
    private readonly IAutomaticFlowDriver _automaticFlow;
    private readonly IRunCombatResolutionCommitter _resolutionCommitter;
    private readonly ConcurrentDictionary<Guid, object> _runLocks = new();

    public CombatRunCoordinator(
        ICombatFactory combatFactory,
        IRunEncounterRuntime runManager,
        ICombatFlowPlanner flowPlanner,
        ICombatCommandHandler commands,
        IAutomaticFlowDriver automaticFlow,
        IOperationalEventBus? eventBus = null)
    {
        _combatFactory = combatFactory;
        _runManager = runManager;
        _eventBus = eventBus;
        _flowPlanner = flowPlanner ?? throw new ArgumentNullException(nameof(flowPlanner));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _automaticFlow = automaticFlow ?? throw new ArgumentNullException(nameof(automaticFlow));
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

            var boundParticipants = CombatParticipantBindings.Resolve(participants, run.PlayerEntityId);
            if (boundParticipants.IsFailure) return Result<CombatRunEncounterResult>.Failure(boundParticipants.Error);
            var resourceValues = initialResourceValues;
            if (initialResourceValues != null)
            {
                var ids = participants.Zip(boundParticipants.Value).ToDictionary(pair => pair.First.InstanceId, pair => pair.Second.InstanceId, StringComparer.Ordinal);
                var remapped = new Dictionary<string, IReadOnlyDictionary<string, float>>(StringComparer.Ordinal);
                foreach (var (ownerId, values) in initialResourceValues)
                {
                    if (!remapped.TryAdd(ids.GetValueOrDefault(ownerId, ownerId), values))
                        return Result<CombatRunEncounterResult>.Failure("Initial resource owner bindings collide");
                }
                resourceValues = remapped;
            }
            var seed = run.Determinism.DrawUInt64();
            var combatResult = _combatFactory.Create(
                boundParticipants.Value,
                new CombatStartOptions(
                    seed.Value,
                    run.Determinism.ContentRevision,
                    runId,
                    node.NodeId,
                    $"run-combat:{runId:N}:{node.NodeId}",
                    InitialResourceValues: resourceValues,
                    ConfigName: run.ConfigName));
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
                    initialStatusEffects,
                    ConfigName: run.ConfigName));
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
            return Result<CombatRunActionResult>.Failure(
                $"Combat is not the active encounter for run {runId}: {combatId}");
        if (!encounter.Combat.IsActive)
            return Result<CombatRunActionResult>.Failure($"Combat is not active: {combatId}");

        var versionValidation = ValidateRunVersion(run, commandIdentity, useCombatStep: true);
        if (versionValidation.IsFailure)
            return Result<CombatRunActionResult>.Failure(versionValidation.Error);

        var handled = _commands.Handle(new CombatCommandHandlingRequest
        {
            CombatId = combatId,
            Run = run,
            Combat = encounter.Combat,
            Command = command,
            Origin = CombatCommandOrigin.PlayerInput
        });
        if (handled.IsFailure)
            return Result<CombatRunActionResult>.Failure(handled.Error);

        var driven = _automaticFlow.Drive(new AutomaticFlowRequest
        {
            CombatId = combatId,
            Run = handled.Value.NextRun,
            Combat = handled.Value.Step.Combat,
            RequestsActivationAdvance = handled.Value.RequestsActivationAdvance
        });
        if (driven.IsFailure)
            return Result<CombatRunActionResult>.Failure(driven.Error);

        var effectiveIdentity = commandIdentity ?? CreateImplicitIdentity(run, encounter.Combat, command);
        var rootPayload = commandPayload.ValueKind == JsonValueKind.Undefined
            ? handled.Value.Step.Payload
            : commandPayload.Clone();
        var steps = new[] { handled.Value.Step }.Concat(driven.Value.Steps).ToArray();
        var committed = _resolutionCommitter.CommitCombatResolution(new CombatResolutionCommit
        {
            RunId = run.RunId,
            ExpectedSequence = run.Sequence,
            PreviousCombat = encounter.Combat,
            RootCommand = effectiveIdentity,
            RootPayload = rootPayload,
            Steps = steps
        });
        if (committed.IsFailure)
            return Result<CombatRunActionResult>.Failure(committed.Error);

        if (handled.Value.Candidate.CardPlay != null)
            PublishCardAction(handled.Value.Candidate.CardPlay);
        return Result<CombatRunActionResult>.Success(new CombatRunActionResult
        {
            CombatState = driven.Value.Combat,
            RunState = committed.Value,
            ConsumedCardId = handled.Value.ConsumedCardId
        });
    }

    private Result<CombatInitializationResult> InitializeCanonicalFlow(RunState run, CombatState combat)
    {
        if (run.ResolvedMode == null)
            return Result<CombatInitializationResult>.Failure(
                "Run has no resolved game mode; canonical combat cannot initialize");
        if (_flowPlanner == null) return Result<CombatInitializationResult>.Failure("Canonical combat flow planner is unavailable");
        var player = PersistentPlayerTransitions.Materialize(run, combat);
        if (player.IsFailure) return Result<CombatInitializationResult>.Failure(player.Error);
        combat = player.Value;
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
        CardZoneSteps = initialized.CardZoneSteps,
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
