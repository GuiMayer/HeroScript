using System.Collections.Immutable;
using Core.Common;

namespace Core.Calculations;

public interface ICalculationEngine
{
    Result<CalculationResult> Calculate(
        CalculationRequest request,
        CalculationPipelineDefinition pipeline);
}

/// <summary>
/// Generic deterministic bucket reducer. It has no knowledge of damage,
/// health, cards, actors or any particular game rule.
/// </summary>
public sealed class CalculationEngine : ICalculationEngine
{
    public Result<CalculationResult> Calculate(
        CalculationRequest request,
        CalculationPipelineDefinition pipeline)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pipeline);
        var validation = Validate(request, pipeline);
        if (validation.IsFailure)
            return Result<CalculationResult>.Failure(validation.Error);

        var current = request.BaseValue;
        var traces = ImmutableArray.CreateBuilder<CalculationBucketTrace>();
        foreach (var bucket in pipeline.Buckets
                     .OrderBy(item => item.Order)
                     .ThenBy(item => item.BucketId, StringComparer.Ordinal))
        {
            var influences = request.Influences
                .Where(item => string.Equals(item.Bucket, bucket.BucketId, StringComparison.Ordinal))
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.SourceKind)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.InfluenceId, StringComparer.Ordinal)
                .ToImmutableArray();
            var input = current;
            current = ApplyBucket(current, bucket.Operation, influences);
            var beforeBounds = current;
            if (bucket.Minimum is { } minimum)
                current = MathF.Max(current, minimum);
            if (bucket.Maximum is { } maximum)
                current = MathF.Min(current, maximum);
            current = ApplyRounding(current, bucket.Rounding);
            traces.Add(new CalculationBucketTrace
            {
                BucketId = bucket.BucketId,
                Order = bucket.Order,
                Operation = bucket.Operation,
                Input = input,
                Contributions = influences.Select(item => new CalculationContributionTrace
                {
                    InfluenceId = item.InfluenceId,
                    SourceKind = item.SourceKind,
                    SourceId = item.SourceId,
                    Value = item.Value,
                    Priority = item.Priority
                }).ToImmutableArray(),
                OutputBeforeBounds = beforeBounds,
                Output = current
            });
        }

        var traceArray = traces.ToImmutable();
        var payload = new CalculationFingerprintPayload(
            request.CalculationId,
            pipeline.PipelineId,
            request.Channel,
            request.BaseValue,
            current,
            traceArray);
        return Result<CalculationResult>.Success(new CalculationResult
        {
            CalculationId = request.CalculationId,
            PipelineId = pipeline.PipelineId,
            Channel = request.Channel,
            BaseValue = request.BaseValue,
            Value = current,
            Buckets = traceArray,
            Fingerprint = payload.Compute()
        });
    }

    private static Result Validate(
        CalculationRequest request,
        CalculationPipelineDefinition pipeline)
    {
        if (string.IsNullOrWhiteSpace(request.CalculationId))
            return Result.Failure("CalculationId is required");
        if (string.IsNullOrWhiteSpace(request.Channel))
            return Result.Failure("Calculation channel is required");
        if (string.IsNullOrWhiteSpace(pipeline.PipelineId))
            return Result.Failure("PipelineId is required");
        if (!string.Equals(request.Channel, pipeline.Channel, StringComparison.Ordinal))
            return Result.Failure(
                $"Calculation channel {request.Channel} does not match pipeline channel {pipeline.Channel}");
        if (!IsFinite(request.BaseValue))
            return Result.Failure("Calculation base value must be finite");
        if (pipeline.Buckets.Count == 0)
            return Result.Failure($"Pipeline {pipeline.PipelineId} has no buckets");
        if (pipeline.Buckets.Any(bucket => string.IsNullOrWhiteSpace(bucket.BucketId)))
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains a bucket without bucketId");
        var duplicate = pipeline.Buckets
            .GroupBy(bucket => bucket.BucketId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains duplicate bucket {duplicate.Key}");
        if (pipeline.Buckets.GroupBy(bucket => bucket.Order).Any(group => group.Count() > 1))
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains duplicate bucket order");
        if (pipeline.Buckets.Any(bucket => bucket.Minimum > bucket.Maximum))
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains invalid bucket bounds");

        var buckets = pipeline.Buckets.Select(bucket => bucket.BucketId).ToHashSet(StringComparer.Ordinal);
        foreach (var influence in request.Influences)
        {
            if (string.IsNullOrWhiteSpace(influence.InfluenceId) ||
                string.IsNullOrWhiteSpace(influence.SourceId))
                return Result.Failure("Every calculation influence requires influenceId and sourceId");
            if (!string.Equals(influence.Channel, request.Channel, StringComparison.Ordinal))
                return Result.Failure($"Influence {influence.InfluenceId} targets another channel");
            if (!buckets.Contains(influence.Bucket))
                return Result.Failure(
                    $"Influence {influence.InfluenceId} targets unknown bucket {influence.Bucket}");
            if (!IsFinite(influence.Value))
                return Result.Failure($"Influence {influence.InfluenceId} value must be finite");
        }
        return Result.Success();
    }

    private static float ApplyBucket(
        float current,
        CalculationBucketOperation operation,
        ImmutableArray<CalculationInfluence> influences) => operation switch
        {
            CalculationBucketOperation.Add => current + influences.Sum(item => item.Value),
            CalculationBucketOperation.AddPercent => current * (1 + influences.Sum(item => item.Value)),
            CalculationBucketOperation.Multiply => influences.Aggregate(
                current,
                (value, influence) => value * influence.Value),
            CalculationBucketOperation.Set => influences.Aggregate(
                current,
                (_, influence) => influence.Value),
            CalculationBucketOperation.Minimum => influences.Aggregate(
                current,
                (value, influence) => MathF.Min(value, influence.Value)),
            CalculationBucketOperation.Maximum => influences.Aggregate(
                current,
                (value, influence) => MathF.Max(value, influence.Value)),
            _ => current
        };

    private static float ApplyRounding(float value, CalculationRounding rounding) => rounding switch
    {
        CalculationRounding.Floor => MathF.Floor(value),
        CalculationRounding.Ceiling => MathF.Ceiling(value),
        CalculationRounding.Round => MathF.Round(value),
        _ => value
    };

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
