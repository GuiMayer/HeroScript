using Core.Common;
using Core.Run;

namespace Core.GameplayEvents;

/// <summary>Creates the private root from an actual command identity. No mutation, publication or persistence.</summary>
public static class GameplayCommandObservation
{
    public static Result<GameplayEffectObservation> Create(RunState run, Guid? combatId, RunCommandIdentity identity)
    {
        if (run == null || identity == null || identity.CommandId == Guid.Empty)
            return Result<GameplayEffectObservation>.Failure("A captured run and command identity are required");
        var catalog = GameplayCoreCatalog.Create(run.SettingId, run.Determinism.ContentRevision);
        if (catalog.IsFailure) return Result<GameplayEffectObservation>.Failure(catalog.Error);
        var transaction = GameplayEventTransaction.Create(new() { SettingId = run.SettingId, ContentRevision = run.Determinism.ContentRevision,
            RunId = (run.Lineage?.RootRunId ?? run.RunId).ToString("D"), BranchId = run.RunId.ToString("D"), CombatId = combatId?.ToString("D"),
            ActionId = identity.CommandId.ToString("D"), CommandId = identity.CommandId.ToString("D") }, catalog.Value, allowedAncestorBranches: run.Lineage?.CausalAncestorRunIds?.Select(id => id.ToString("D")));
        return transaction.IsFailure ? Result<GameplayEffectObservation>.Failure(transaction.Error)
            : Result<GameplayEffectObservation>.Success(new() { Events = transaction.Value, PublisherId = GameplayCoreCatalog.PublisherId,
                ReasonId = "effect.application", CostReasonId = "cost.payment", RefundReasonId = "cost.refund" });
    }
}
