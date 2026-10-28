using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Common;
using Core.GameplayEvents;
using Core.Run;

namespace Core.Combat;

public sealed partial class CombatRunCoordinator
{
    /// <summary>Observes the canonical initialization candidate after factory materialization, before attachment or commit.</summary>
    public Result<ObservedGameplayExecution<CombatInitializationResult>> InitializeObserved(RunState run, CombatState combat,
        GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != run.Determinism.ContentRevision ||
            observation.Events.Scope.BranchId != run.RunId.ToString("D") || observation.Events.Scope.SettingId != run.SettingId ||
            observation.Events.Scope.CombatId != combat.CombatId.ToString("D") || combat.RunId != run.RunId)
            return Result<ObservedGameplayExecution<CombatInitializationResult>>.Failure("Initialization observation belongs to a different aggregate or revision");
        var cursor = new GameplayRunObservation(observation);
        var result = InitializeCanonicalFlow(run, combat, cursor);
        return result.IsFailure ? Result<ObservedGameplayExecution<CombatInitializationResult>>.Failure(result.Error)
            : Result<ObservedGameplayExecution<CombatInitializationResult>>.Success(new(result.Value, cursor.Current));
    }
}
