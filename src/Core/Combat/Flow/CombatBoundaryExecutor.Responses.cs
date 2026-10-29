using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Run;

namespace Core.Combat.Flow;

public sealed partial class CombatBoundaryExecutor
{
    private readonly IGameplayOccurrenceExecutor? _occurrences;

    private Result<T> WithOccurrenceObservation<T>(RunState run, CombatState combat, Func<ObservationCursor?, Result<T>> execute)
    {
        if (_occurrences == null) return execute(null);
        var prepared = _occurrences.PrepareOccurrenceObservation(run, combat);
        if (prepared.IsFailure) return Result<T>.Failure(prepared.Error);
        if (prepared.Value == null) return execute(null);
        var result = Observe(run, prepared.Value, execute);
        return result.IsFailure ? Result<T>.Failure(result.Error) : Result<T>.Success(result.Value.Execution);
    }

    private static EffectBatchResult BoundaryResponses(ObservationCursor? observation, RunState run, CombatState combat) =>
        observation?.Responses(run, combat) ?? new() { Run = run, State = combat };
}
