using System.Collections.Immutable;
using System.Text.Json;
using Core.Combat.Gambits;
using Core.Combat.LegalActions;
using Core.Combat.Models;
using Core.Common;
using Core.Run;
using Core.StatusEffects;
using Core.GameplayEvents;

namespace Core.Combat.Flow;

public sealed partial class AutomaticFlowDriver
{
    public Result<ObservedGameplayExecution<AutomaticFlowResult>> DriveObserved(AutomaticFlowRequest request, GameplayEffectObservation observation)
    {
        if (observation?.Events == null || observation.Events.Scope.ContentRevision != request.Run.Determinism.ContentRevision)
            return Result<ObservedGameplayExecution<AutomaticFlowResult>>.Failure("Automatic observation requires the action revision");
        var current = observation;
        var result = DriveCore(request, ref current);
        return result.IsFailure ? Result<ObservedGameplayExecution<AutomaticFlowResult>>.Failure(result.Error)
            : Result<ObservedGameplayExecution<AutomaticFlowResult>>.Success(new(result.Value, current!));
    }

}
