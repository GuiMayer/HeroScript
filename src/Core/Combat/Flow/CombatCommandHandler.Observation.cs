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

public sealed partial class CombatActionStateReducer
{
    public Result<ObservedGameplayExecution<CombatActionReduction>> ApplyObserved(RunState run, CombatState previousCombat, LegalActionCandidate candidate,
        CombatActionCommand rootCommand, CombatFlowPoliciesDefinition policies, GameplayEffectObservation observation)
    {
        var result = ApplyCore(run, previousCombat, candidate, rootCommand, policies, observation);
        return result.IsFailure ? Result<ObservedGameplayExecution<CombatActionReduction>>.Failure(result.Error)
            : Result<ObservedGameplayExecution<CombatActionReduction>>.Success(new(result.Value, result.Value.Observation!));
    }

}

public sealed partial class CombatCommandHandler
{
    public Result<ObservedGameplayExecution<CombatCommandHandlingResult>> HandleObserved(CombatCommandHandlingRequest request, GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != request.Run.Determinism.ContentRevision)
            return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure("Command observation requires the action revision");
        var action = request.Command.PowerId ?? request.Command.ActionType.ToString();
        var started = GameplayActionObservation.Begin(observation, action, request.Command.ActorId);
        if (started.IsFailure) return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure(started.Error);
        var result = HandleCore(request, started.Value);
        if (result.IsFailure) return Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure(result.Error);
        var completed = GameplayActionObservation.Complete(observation, started.Value, result.Value.Observation!, action, request.Command.ActorId);
        return completed.IsFailure ? Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Failure(completed.Error)
            : Result<ObservedGameplayExecution<CombatCommandHandlingResult>>.Success(new(result.Value with { Observation = completed.Value }, completed.Value));
    }

}
