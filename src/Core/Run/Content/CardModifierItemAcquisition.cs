using Core.Common;
using Core.Content;

namespace Core.Run.Content;

public static class CardModifierItemAcquisition
{
    public static Result<RunState> Grant(RunState run, ContentRuntime runtime, string definitionId, string source)
    {
        if (run.Lifecycle != RunLifecycleState.Active || run.ActiveEncounterId != null ||
            runtime.Manifest.Revision != run.Determinism.ContentRevision || runtime.Manifest.ConfigName != run.ConfigName)
            return Result<RunState>.Failure("Modifier acquisition requires the run's published setting outside combat");
        var definition = runtime.GetDefinition<CardModifierItemDefinition>("card-modifier-items", definitionId);
        if (definition.IsFailure) return Result<RunState>.Failure(definition.Error);
        var valid = definition.Value.Validate(runtime);
        if (valid.IsFailure) return Result<RunState>.Failure(valid.Error);
        valid = RunEligibility.Require(run, Core.Meta.UnlockTargetKind.CardUpgrade, definition.Value.UpgradeId);
        if (valid.IsFailure) return Result<RunState>.Failure(valid.Error);
        var allocated = run.Determinism.AllocateId("card-modifier-item");
        var inventory = (run.CardModifierItems ?? new()).Add(new() { ItemInstanceId = allocated.Value,
            ItemDefinitionId = definitionId, AcquisitionRevision = runtime.Manifest.Revision,
            AcquisitionSource = source, AcquisitionSequence = run.Sequence + 1 });
        return inventory.IsFailure ? Result<RunState>.Failure(inventory.Error) :
            Result<RunState>.Success(run with { CardModifierItems = inventory.Value, Determinism = allocated.Context });
    }
}
