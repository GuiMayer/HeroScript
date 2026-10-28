using Core.Combat.Models;
using Core.Common;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;
using Core.Run;

namespace Core.Effects;

public interface IGameplayOccurrenceExecutor
{
    Result<GameplayEffectObservation?> PrepareOccurrenceObservation(RunState run, CombatState combat, GameplayEffectObservation? observation = null);
    Result<ObservedEffectExecution> DispatchOccurrencesObserved(RunState beforeRun, CombatState beforeCombat,
        RunState afterRun, CombatState afterCombat, GameplayEffectObservation before, GameplayEffectObservation after);
}

public sealed partial class EffectTriggerExecutor : IGameplayOccurrenceExecutor
{
    public Result<GameplayEffectObservation?> PrepareOccurrenceObservation(RunState run, CombatState combat, GameplayEffectObservation? observation = null)
    {
        if (observation != null) return Result<GameplayEffectObservation?>.Success(observation);
        var authored = HasAuthoredResponses(new() { Run = run, Combat = combat });
        if (authored.IsFailure) return Result<GameplayEffectObservation?>.Failure(authored.Error);
        if (!authored.Value) return Result<GameplayEffectObservation?>.Success(null);
        var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision);
        if (catalog.IsFailure) return Result<GameplayEffectObservation?>.Failure(catalog.Error);
        var id = Core.Determinism.CanonicalJson.ComputeHash(new { run.RunId, run.Sequence, combat, operation = "gameplay" });
        var transaction = GameplayEventTransaction.Create(new() { SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
            RunId = (run.Lineage?.RootRunId ?? run.RunId).ToString("D"), BranchId = run.RunId.ToString("D"),
            CombatId = combat.CombatId.ToString("D"), ActionId = id, CommandId = id }, catalog.Value);
        return transaction.IsFailure ? Result<GameplayEffectObservation?>.Failure(transaction.Error)
            : Result<GameplayEffectObservation?>.Success(new() { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId,
                ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" });
    }

    private static Result<GameplayResponseQuotas> ResumeQuotas(GameplayEffectObservation observation,
        int maximum = GameplayResponseQuotas.MaximumInvocations)
    {
        if (observation.ResponseProgress is not { } progress) return GameplayResponseQuotas.Begin(observation.Events.Scope, maximum);
        return progress.Quotas.Scope != observation.Events.Scope || progress.MaximumInvocations != maximum ||
            progress.Work is < 0 or > EffectExecutionLimits.MaximumSteps
            ? Result<GameplayResponseQuotas>.Failure("Response continuation belongs to another action or budget")
            : Result<GameplayResponseQuotas>.Success(progress.Quotas);
    }

    private static GameplayEffectObservation? CaptureProgress(GameplayEffectObservation? observation, ExecutionBudget budget) => observation == null ? null
        : budget.Responses != null ? observation with { ResponseProgress = new(budget.Responses.Quotas, budget.Work, budget.MaximumInvocations) }
        : observation.ResponseProgress is { } progress ? observation with { ResponseProgress = progress with { Work = budget.Work } } : observation;

    /// <summary>Executes responses to already produced facts using their actual before/after snapshots.
    /// Producers own the mutation; this operation neither fabricates a fact nor commits the candidate.</summary>
    public Result<ObservedEffectExecution> DispatchOccurrencesObserved(RunState beforeRun, CombatState beforeCombat,
        RunState afterRun, CombatState afterCombat, GameplayEffectObservation before, GameplayEffectObservation after)
    {
        if (before?.Events == null || after?.Events == null || before.Events.Scope != after.Events.Scope ||
            before.Events.Occurrences.Count > after.Events.Occurrences.Count || before.Events.Occurrences
                .Where((fact, index) => !ReferenceEquals(fact, after.Events.Occurrences[index])).Any())
            return Result<ObservedEffectExecution>.Failure("Occurrence dispatch requires an append-only causal prefix");
        var request = new EffectTriggerExecutionRequest { Run = afterRun, Combat = afterCombat };
        if (beforeRun.RunId != afterRun.RunId || beforeCombat.CombatId != afterCombat.CombatId ||
            beforeRun.SettingId != afterRun.SettingId || beforeRun.Determinism.ContentRevision != afterRun.Determinism.ContentRevision)
            return Result<ObservedEffectExecution>.Failure("Occurrence dispatch cannot substitute its aggregate or revision");
        try
        {
            var prepared = PrepareResourceResponses(request, after);
            if (prepared.IsFailure) return Result<ObservedEffectExecution>.Failure(prepared.Error);
            var result = DispatchResponses(prepared.Value.Budget, true, prepared.Value.Observation,
                before.Events.Occurrences.Count, beforeCombat, beforeRun, afterCombat, afterRun, 0, null);
            if (result.IsFailure) return Result<ObservedEffectExecution>.Failure(result.Error);
            var execution = result.Value ?? new EffectBatchResult { Run = afterRun, State = afterCombat, Observation = after };
            return Result<ObservedEffectExecution>.Success(new(execution, execution.Observation!));
        }
        catch (GameplayQueryException exception) { return Result<ObservedEffectExecution>.Failure(exception.Message); }
    }
}
