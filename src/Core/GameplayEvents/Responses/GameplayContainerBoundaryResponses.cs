using System.Collections.Immutable;
using Core.Common;
using Core.Effects;

namespace Core.GameplayEvents.Responses;

/// <summary>Explicit scheduling at an existing container boundary, separate from global post-fact broadcasts.</summary>
internal static class GameplayContainerBoundaryResponses
{
    internal static Result Validate(GameplayResponseDefinition response, string container = "status")
    {
        if (response == null || response.Timing != ResponseExecutionTiming.ContainerBoundary || response.Observe?.Boundary is not { } boundary ||
            boundary.BoundaryIds.IsDefaultOrEmpty || boundary.BoundaryIds.Length != 1 ||
            !(container == "relic" ? new[] { "CombatStart", "CombatEnd", "StartActivation", "EndActivation", "StartRound", "EndRound" }
                : new[] { "StartActivation", "EndActivation", "StartRound", "EndRound" }).Contains(boundary.BoundaryIds[0], StringComparer.Ordinal) ||
            response.Observe.Resource != null || response.Frequency.Scope != ResponseFrequencyScope.Occurrence)
            return Result.Failure("Container boundary timing requires an explicit supported boundary and occurrence frequency");
        var expected = GameplayBoundarySources.Kind(boundary.BoundaryIds[0]);
        var kinds = HierarchySelector.Compile(response.Observe.Kinds.Include, response.Observe.Kinds.Exclude);
        return kinds.IsFailure || !kinds.Value.Matches(HierarchicalName.Parse(expected).Value)
            ? Result.Failure("Container boundary selector cannot match its configured boundary") : Result.Success();
    }

    internal static Result ValidatePhase(GameplayResponseDefinition response, string phaseId)
    {
        if (response == null || response.Timing != ResponseExecutionTiming.ContainerBoundary || response.Observe?.Boundary is not { } boundary ||
            boundary.BoundaryIds.IsDefault || boundary.BoundaryIds.Length != 1 || boundary.BoundaryIds[0] != phaseId ||
            response.Frequency.Scope != ResponseFrequencyScope.Occurrence || response.Observe.Resource != null)
            return Result.Failure("Phase response requires its own phase boundary and occurrence frequency");
        var kinds = HierarchySelector.Compile(response.Observe.Kinds.Include, response.Observe.Kinds.Exclude);
        return kinds.IsFailure || !new[] { "phase.entered", "phase.exited" }.Any(kind => kinds.Value.Matches(HierarchicalName.Parse(kind).Value))
            ? Result.Failure("Phase response selector cannot match entering or exiting its phase") : Result.Success();
    }

    internal static Result<ImmutableArray<(EffectTriggerDefinition Trigger, GameplayResponseDefinition? Response)>> Triggers(
        IReadOnlyList<EffectTriggerDefinition> historical, ImmutableArray<GameplayResponseDefinition>? responses, string container = "status")
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
            var valid = Validate(response, container);
            if (valid.IsFailure) return Result<ImmutableArray<(EffectTriggerDefinition, GameplayResponseDefinition?)>>.Failure(valid.Error);
            result.Add((new() { TriggerId = response.ResponseId, Boundary = response.Observe.Boundary!.BoundaryIds[0],
                Priority = response.Order.Priority, Effects = response.Effects }, response));
        }
        return Result<ImmutableArray<(EffectTriggerDefinition, GameplayResponseDefinition?)>>.Success(result.ToImmutable());
    }
}
