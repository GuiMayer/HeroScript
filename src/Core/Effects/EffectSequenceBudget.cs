using System.Collections.Immutable;
using Core.Calculations;
using Core.Common;

namespace Core.Effects;

/// <summary>Opt-in: Repeat allocates one source budget, rather than recalculating a complete effect.</summary>
public sealed record EffectSequenceDistribution
{
    public ImmutableArray<string> SourceStageIds { get; init; } = [];
    public CalculationDistributionPolicy Allocation { get; init; } = new();
}

public sealed record EffectSequenceBudget
{
    public EffectNumericParameter Parameter { get; init; }
    public CalculationResult Capture { get; init; } = new();
    public CalculationDistributionResult Allocation { get; init; } = new();
}

public sealed record EffectImpactShare
{
    public EffectNumericParameter Parameter { get; init; }
    public string DistributionId { get; init; } = string.Empty;
    public CalculationDistributionShare Share { get; init; } = new();
}

/// <summary>Gameplay scheduling only. All arithmetic belongs to the calculation engine.</summary>
internal sealed class EffectSequenceBudgetPlanner(ICalculationResolver resolver, ICalculationEngine calculations)
{
    internal Result<ImmutableArray<EffectSequenceBudget>> Capture(EffectTriggerExecutionRequest request,
        EffectDefinition effect, string sequenceId, string componentId)
    {
        var source = request.Combat.GetActor(request.SourceEntityId) ?? request.Combat.GetActor(request.OwnerEntityId)!;
        // A source budget must not accidentally read the first target, nor caller-supplied target variables.
        var variables = GameplayFormulaContext.Build(source, null, request.Combat.GetActor(request.OwnerEntityId)!,
            request.Run, request.Variables.Where(pair => !pair.Key.StartsWith("target.", StringComparison.OrdinalIgnoreCase) &&
                pair.Key is not ("repeat_index" or "target_index")).ToDictionary());
        var budgets = ImmutableArray.CreateBuilder<EffectSequenceBudget>();
        foreach (var parameter in effect.Parameters.Where(item => item.Distribution != null).OrderBy(item => item.Parameter))
        {
            var policy = parameter.Distribution!;
            CalculationQuantity? input = null;
            if (parameter.InputQuantityId is { } inputId && !request.Quantities.TryGetValue(inputId, out input))
                return Result<ImmutableArray<EffectSequenceBudget>>.Failure($"Unknown sequence budget input: {inputId}");
            var captured = resolver.ResolveParameter(effect, parameter with { StageIds = policy.SourceStageIds, Distribution = null },
                $"{sequenceId}:{parameter.Parameter}:capture", new()
                {
                    ContentRevision = request.ContentRevision,
                    Run = request.Run,
                    Combat = request.Combat,
                    Actor = source,
                    Card = request.Card,
                    ComponentId = componentId,
                    CaptureOnly = true,
                    InputQuantity = input,
                    Variables = variables,
                    Tags = request.Tags.Concat(effect.Tags).ToHashSet(StringComparer.Ordinal)
                });
            if (captured.IsFailure) return Result<ImmutableArray<EffectSequenceBudget>>.Failure(captured.Error);
            if (captured.Value.Calculation == null || captured.Value.Pipeline == null)
                return Result<ImmutableArray<EffectSequenceBudget>>.Failure("Sequence distribution requires a configured staged pipeline");
            var profile = ValidateProfile(parameter, captured.Value.Pipeline);
            if (profile.IsFailure) return Result<ImmutableArray<EffectSequenceBudget>>.Failure(profile.Error);
            var allocated = calculations.Distribute(new()
            {
                DistributionId = $"{sequenceId}:{parameter.Parameter}",
                ContentRevision = request.ContentRevision,
                UnitId = parameter.UnitId,
                InputQuantity = captured.Value.Calculation.Quantity,
                Policy = policy.Allocation,
                Recipients = Enumerable.Range(0, effect.Repeat).Select(index => new CalculationDistributionRecipient
                { RecipientId = $"impact:{index}", Order = index }).ToArray()
            });
            if (allocated.IsFailure) return Result<ImmutableArray<EffectSequenceBudget>>.Failure(allocated.Error);
            budgets.Add(new() { Parameter = parameter.Parameter, Capture = captured.Value.Calculation, Allocation = allocated.Value });
        }
        return Result<ImmutableArray<EffectSequenceBudget>>.Success(budgets.ToImmutable());
    }

    internal static Result ValidateProfile(EffectNumericParameterDefinition parameter, CalculationPipelineDefinition pipeline)
    {
        if (parameter.Distribution is not { } policy) return Result.Success();
        var valid = CalculationEngine.ValidateDefinition(pipeline);
        if (valid.IsFailure) return valid;
        var ordered = pipeline.Buckets.OrderBy(bucket => bucket.Order).ThenBy(bucket => bucket.BucketId, StringComparer.Ordinal)
            .Select(bucket => bucket.StageId ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
        var sourceIds = policy.SourceStageIds.ToHashSet(StringComparer.Ordinal);
        var impactIds = parameter.StageIds.ToHashSet(StringComparer.Ordinal);
        if (pipeline.Stages.IsEmpty || pipeline.UnitId != parameter.UnitId || pipeline.Channel != parameter.Channel ||
            ordered.Any(string.IsNullOrWhiteSpace) || sourceIds.Count == 0 || impactIds.Count == 0 || sourceIds.Overlaps(impactIds) ||
            !sourceIds.SetEquals(ordered.Take(sourceIds.Count)) || !impactIds.SetEquals(ordered.Skip(sourceIds.Count)) ||
            pipeline.Stages.Any(stage => sourceIds.Contains(stage.StageId) && stage.Scope == CalculationStageScope.Target ||
                impactIds.Contains(stage.StageId) && stage.Scope == CalculationStageScope.Actor))
            return Result.Failure("Sequence distribution requires a source prefix and disjoint impact suffix covering a compatible staged pipeline");
        return Result.Success();
    }
}
