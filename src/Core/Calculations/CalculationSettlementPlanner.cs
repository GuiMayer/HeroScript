using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Common;
using Core.Effects;
using Core.Resources;

namespace Core.Calculations;

/// <summary>
/// A concrete state change derived from a calculation trace. The calculation
/// engine remains unaware of entities and resources; this planner maps a
/// configured influence back to the state that supplied it.
/// </summary>
public sealed record ResolvedCalculationSettlement
{
    public string SettlementId { get; init; } = string.Empty;
    public string InfluenceId { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string ResourceId { get; init; } = string.Empty;
    public ResourceValueField Field { get; init; } = ResourceValueField.Current;
    public ResourceEffectOperation Operation { get; init; } = ResourceEffectOperation.SUBTRACT;
    public float Value { get; init; }
}

public interface ICalculationSettlementPlanner
{
    Result<IReadOnlyList<ResolvedCalculationSettlement>> Plan(
        CalculationResult calculation,
        CalculationPipelineDefinition pipeline,
        CalculationSourceContext context);
}

public sealed class CalculationSettlementPlanner : ICalculationSettlementPlanner
{
    public Result<IReadOnlyList<ResolvedCalculationSettlement>> Plan(
        CalculationResult calculation,
        CalculationPipelineDefinition pipeline,
        CalculationSourceContext context)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(calculation.PipelineId, pipeline.PipelineId, StringComparison.Ordinal) ||
            !string.Equals(calculation.PipelineFingerprint,
                Core.Determinism.CanonicalJson.ComputeHash(pipeline), StringComparison.Ordinal))
            return Result<IReadOnlyList<ResolvedCalculationSettlement>>.Failure(
                "Calculation result does not match settlement pipeline");

        var result = ImmutableArray.CreateBuilder<ResolvedCalculationSettlement>();
        foreach (var binding in pipeline.ResourceInfluenceBindings
                     .Where(item => item.Settlement != null)
                     .OrderByDescending(item => item.Priority)
                     .ThenBy(item => item.BindingId, StringComparer.Ordinal))
        {
            var contribution = calculation.Buckets
                .Where(bucket => string.Equals(bucket.BucketId, binding.Bucket, StringComparison.Ordinal))
                .SelectMany(bucket => bucket.Contributions)
                .SingleOrDefault(item => string.Equals(item.InfluenceId, binding.BindingId, StringComparison.Ordinal));
            if (contribution is not { Applied: true })
                continue;
            var value = binding.Settlement!.UseEffectiveValue
                ? contribution.EffectiveValue
                : contribution.Value;
            if (value is null || !float.IsFinite(value.Value) || value.Value < 0)
                return Result<IReadOnlyList<ResolvedCalculationSettlement>>.Failure(
                    $"Settlement {binding.BindingId} produced an invalid value");
            if (value.Value == 0)
                continue;
            var entity = binding.Scope == CalculationEntityScope.Actor ? context.Actor : context.Target;
            if (entity == null)
                return Result<IReadOnlyList<ResolvedCalculationSettlement>>.Failure(
                    $"Settlement {binding.BindingId} has no scoped entity");
            result.Add(new()
            {
                SettlementId = $"{calculation.CalculationId}:settlement:{binding.BindingId}",
                InfluenceId = binding.BindingId,
                EntityId = entity.InstanceId,
                ResourceId = binding.ResourceId,
                Field = binding.Settlement.Field,
                Operation = binding.Settlement.Operation,
                Value = value.Value
            });
        }
        return Result<IReadOnlyList<ResolvedCalculationSettlement>>.Success(result.ToImmutable());
    }
}
