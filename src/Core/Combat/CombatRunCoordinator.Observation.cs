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
using Core.GameplayEvents;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Core.Combat;

public sealed partial class CombatRunCoordinator
{
    /// <summary>Calculates the same complete action candidate under the coordinator gate, without commit, publication or receipt mutation.</summary>
    public Result<ObservedGameplayExecution<CombatResolutionCommit>> PlanObservedAction(Guid runId, Guid combatId,
        CombatActionCommand command, RunCommandIdentity identity, JsonElement payload = default)
    {
        var runLock = _runLocks.GetOrAdd(runId, _ => new object());
        lock (runLock)
        {
            var loaded = _runManager.GetRun(runId);
            if (loaded.IsFailure) return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(loaded.Error);
            var run = loaded.Value;
            var expectedType = command.ActionType == ActionType.PLAY_CARD ? GameplayCommandTypes.PlayCard
                : command.ActionType == ActionType.END_TURN ? GameplayCommandTypes.EndTurn : GameplayCommandTypes.ExecuteAction;
            if (identity.CommandId == Guid.Empty || identity.Type != expectedType || command.RunId != Guid.Empty && command.RunId != runId)
                return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure("Observed command identity does not match its route or aggregate");
            var encounter = run.GetActiveEncounter();
            if (encounter == null || encounter.Combat.CombatId != combatId || !encounter.Combat.IsActive)
                return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure("Combat is not an active encounter for this run");
            var version = ValidateRunVersion(run, identity, useCombatStep: true);
            if (version.IsFailure) return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(version.Error);
            var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision);
            if (catalog.IsFailure) return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(catalog.Error);
            var transaction = GameplayEventTransaction.Create(new()
            {
                SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
                RunId = (run.Lineage?.RootRunId ?? run.RunId).ToString("D"), BranchId = run.RunId.ToString("D"),
                ActionId = identity.CommandId.ToString("D"), CommandId = identity.CommandId.ToString("D"), CombatId = combatId.ToString("D")
            }, catalog.Value);
            if (transaction.IsFailure) return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(transaction.Error);
            var observation = new GameplayEffectObservation { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId,
                ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" };
            var started = GameplayActionObservation.Begin(observation, identity.Type, command.ActorId, "command.started", "command.input");
            if (started.IsFailure) return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(started.Error);
            var candidate = PrepareActionCandidate(run, encounter.Combat, combatId, command, identity, payload, started.Value);
            if (candidate.IsFailure) return Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(candidate.Error);
            var completed = GameplayActionObservation.Complete(observation, started.Value, candidate.Value.Observation!, identity.Type,
                command.ActorId, "command.completed", "command.input");
            return completed.IsFailure ? Result<ObservedGameplayExecution<CombatResolutionCommit>>.Failure(completed.Error)
                : Result<ObservedGameplayExecution<CombatResolutionCommit>>.Success(new(candidate.Value.Commit, completed.Value));
        }
    }

    private sealed record ActionCandidate(CombatCommandHandlingResult Handled, AutomaticFlowResult Driven,
        CombatResolutionCommit Commit, GameplayEffectObservation? Observation);

    private Result<ActionCandidate> PrepareActionCandidate(RunState run, CombatState combat, Guid combatId,
        CombatActionCommand command, RunCommandIdentity? identity, JsonElement payload, GameplayEffectObservation? observation)
    {
        var request = new CombatCommandHandlingRequest { CombatId = combatId, Run = run, Combat = combat, Command = command,
            Origin = CombatCommandOrigin.PlayerInput };
        Result<CombatCommandHandlingResult> handled;
        if (observation == null) handled = _commands.Handle(request);
        else
        {
            var observed = _commands.HandleObserved(request, observation);
            if (observed.IsFailure) return Result<ActionCandidate>.Failure(observed.Error);
            observation = observed.Value.Observation;
            handled = Result<CombatCommandHandlingResult>.Success(observed.Value.Execution);
        }
        if (handled.IsFailure) return Result<ActionCandidate>.Failure(handled.Error);
        var driveRequest = new AutomaticFlowRequest { CombatId = combatId, Run = handled.Value.NextRun, Combat = handled.Value.Step.Combat,
            RequestsActivationAdvance = handled.Value.RequestsActivationAdvance };
        Result<AutomaticFlowResult> driven;
        if (observation == null) driven = _automaticFlow.Drive(driveRequest);
        else
        {
            var observed = _automaticFlow.DriveObserved(driveRequest, observation);
            if (observed.IsFailure) return Result<ActionCandidate>.Failure(observed.Error);
            observation = observed.Value.Observation;
            driven = Result<AutomaticFlowResult>.Success(observed.Value.Execution);
        }
        if (driven.IsFailure) return Result<ActionCandidate>.Failure(driven.Error);
        var commit = new CombatResolutionCommit
        {
            RunId = run.RunId, ExpectedSequence = run.Sequence, PreviousCombat = combat,
            RootCommand = identity ?? CreateImplicitIdentity(run, combat, command),
            RootPayload = payload.ValueKind == JsonValueKind.Undefined ? handled.Value.Step.Payload : payload.Clone(),
            Steps = new[] { handled.Value.Step }.Concat(driven.Value.Steps).ToArray()
        };
        return Result<ActionCandidate>.Success(new(handled.Value, driven.Value, commit, observation));
    }

}
