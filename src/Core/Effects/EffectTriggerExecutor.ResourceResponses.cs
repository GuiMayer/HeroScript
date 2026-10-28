using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.GameplayEvents;
using Core.GameplayEvents.Responses;

namespace Core.Effects;

public sealed partial class EffectTriggerExecutor
{
    private static bool HasResourceResponses(EffectTriggerExecutionRequest request) =>
        request.Run?.Relics.Any(relic => relic.Reactions is { IsDefaultOrEmpty: false }) == true;

    private Result<(ExecutionBudget Budget, GameplayEffectObservation? Observation)> PrepareResourceResponses(
        EffectTriggerExecutionRequest request, GameplayEffectObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authored = HasAuthoredResponses(request);
        if (authored.IsFailure) return Result<(ExecutionBudget, GameplayEffectObservation?)>.Failure(authored.Error);
        if (authored.Value && _contentRuntimes == null && request.Run!.Deck.Topology.Instances.Count > 0)
            return Result<(ExecutionBudget, GameplayEffectObservation?)>.Failure("Card observer composition requires captured content runtimes");
        if (!HasResourceResponses(request) && !authored.Value)
            return Result<(ExecutionBudget, GameplayEffectObservation?)>.Success((new(), observation));
        var run = request.Run!;
        var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision);
        if (catalog.IsFailure) return Result<(ExecutionBudget, GameplayEffectObservation?)>.Failure(catalog.Error);
        if (observation != null && (observation.Events.Scope.SettingId != run.SettingId ||
            observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision ||
            observation.Events.Scope.RunId != (run.Lineage?.RootRunId ?? run.RunId).ToString("D") ||
            observation.Events.Scope.BranchId != run.RunId.ToString("D")))
            return Result<(ExecutionBudget, GameplayEffectObservation?)>.Failure("Resource response observation must use the actual pinned run scope");
        if (observation == null)
        {
            var actionId = EffectResultContext.ExecutionId(request);
            var transaction = GameplayEventTransaction.Create(new()
            {
                SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
                RunId = (run.Lineage?.RootRunId ?? run.RunId).ToString("D"), BranchId = run.RunId.ToString("D"),
                ActionId = actionId, CommandId = actionId, CombatId = request.Combat.CombatId.ToString("D")
            }, catalog.Value);
            if (transaction.IsFailure) return Result<(ExecutionBudget, GameplayEffectObservation?)>.Failure(transaction.Error);
            observation = new() { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId,
                ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" };
        }
        var quotas = GameplayResponseQuotas.Begin(observation.Events.Scope);
        if (quotas.IsFailure) return Result<(ExecutionBudget, GameplayEffectObservation?)>.Failure(quotas.Error);
        var providers = authored.Value ? AuthoredProviders().Add(new LegacyResourceReactionObserverProvider()) : [new LegacyResourceReactionObserverProvider()];
        var session = new ResponseExecutionSession(catalog.Value, providers, quotas.Value,
            new GameplayResponseEvaluation(_formulas, _contentRuntimes), request.Card?.CardInstanceId, request.Card?.CardInstanceId);
        return Result<(ExecutionBudget, GameplayEffectObservation?)>.Success((new() { Responses = session }, observation));
    }
}
