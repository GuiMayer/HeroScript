using System.Collections.Immutable;
using Core.Common;
using Core.Effects;

namespace Core.GameplayEvents.Responses;

/// <summary>Explicit scheduling at an existing container boundary, separate from global post-fact broadcasts.</summary>
internal static class GameplayContainerBoundaryResponses
{
    internal static Result Validate(GameplayResponseDefinition response)
    {
        if (response == null || response.Timing != ResponseExecutionTiming.ContainerBoundary || response.Observe?.Boundary is not { } boundary ||
            boundary.BoundaryIds.IsDefaultOrEmpty || boundary.BoundaryIds.Length != 1 ||
            !new[] { "CombatStart", "CombatEnd", "StartActivation", "EndActivation", "StartRound", "EndRound" }.Contains(boundary.BoundaryIds[0], StringComparer.Ordinal) ||
            response.Observe.Resource != null || response.Frequency.Scope != ResponseFrequencyScope.Occurrence)
            return Result.Failure("Container boundary timing requires an explicit supported boundary and occurrence frequency");
        var expected = GameplayBoundarySources.Kind(boundary.BoundaryIds[0]);
        var kinds = HierarchySelector.Compile(response.Observe.Kinds.Include, response.Observe.Kinds.Exclude);
        return kinds.IsFailure || !kinds.Value.Matches(HierarchicalName.Parse(expected).Value)
            ? Result.Failure("Container boundary selector cannot match its configured boundary") : Result.Success();
    }

    internal static Result<ImmutableArray<(EffectTriggerDefinition Trigger, GameplayResponseDefinition? Response)>> Triggers(
        IReadOnlyList<EffectTriggerDefinition> historical, ImmutableArray<GameplayResponseDefinition>? responses)
    {
        if (responses is { } supplied && (supplied.IsDefault || supplied.Any(item => item == null)))
            return Result<ImmutableArray<(EffectTriggerDefinition, GameplayResponseDefinition?)>>.Failure("Boundary response declarations are invalid");
        if (historical.Count > 0 && responses?.Any(item => item.Timing == ResponseExecutionTiming.ContainerBoundary) == true)
            return Result<ImmutableArray<(EffectTriggerDefinition, GameplayResponseDefinition?)>>.Failure("Mixed historical and canonical boundary authoring is ambiguous");
        var result = ImmutableArray.CreateBuilder<(EffectTriggerDefinition, GameplayResponseDefinition?)>();
        result.AddRange(historical.Select(trigger => (trigger, (GameplayResponseDefinition?)null)));
        if (responses is { } declarations)
        foreach (var response in declarations.Where(response => response.Timing == ResponseExecutionTiming.ContainerBoundary))
        {
            var valid = Validate(response);
            if (valid.IsFailure) return Result<ImmutableArray<(EffectTriggerDefinition, GameplayResponseDefinition?)>>.Failure(valid.Error);
            result.Add((new() { TriggerId = response.ResponseId, Boundary = response.Observe.Boundary!.BoundaryIds[0],
                Priority = response.Order.Priority, Effects = response.Effects }, response));
        }
        return Result<ImmutableArray<(EffectTriggerDefinition, GameplayResponseDefinition?)>>.Success(result.ToImmutable());
    }
}
