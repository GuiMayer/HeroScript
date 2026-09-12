using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Math;

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
public sealed class CalculationEngine(IRuntimeFormulaEvaluator? formulas = null) : ICalculationEngine
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
                .ThenBy(item => item.OrderKey, StringComparer.Ordinal)
                .ThenBy(item => item.InfluenceId, StringComparer.Ordinal)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal)
                .ToImmutableArray();
            var input = current;
            if (bucket.Operation == CalculationBucketOperation.Set &&
                bucket.SetConflict == SetConflictPolicy.ErrorOnMultiple && influences.Length > 1)
                return Result<CalculationResult>.Failure($"Bucket {bucket.BucketId} has multiple Set contributions");
            var evaluated = EvaluateBucket(request, bucket, current, influences);
            if (evaluated.IsFailure)
                return Result<CalculationResult>.Failure(evaluated.Error);
            current = evaluated.Value.Value;
            if (!IsFinite(current))
                return Result<CalculationResult>.Failure($"Bucket {bucket.BucketId} produced a non-finite value");
            var beforeBounds = current;
            if (bucket.Minimum is { } minimum)
                current = MathF.Max(current, minimum);
            if (bucket.Maximum is { } maximum)
                current = MathF.Min(current, maximum);
            current = ApplyRounding(current, bucket.Rounding, bucket.MidpointRounding);
            traces.Add(new CalculationBucketTrace
            {
                BucketId = bucket.BucketId,
                Order = bucket.Order,
                Operation = bucket.Operation,
                Formula = bucket.Formula,
                Input = input,
                Contributions = evaluated.Value.Contributions,
                OutputBeforeBounds = beforeBounds,
                Output = current
            });
        }

        var traceArray = traces.ToImmutable();
        var pipelineFingerprint = CanonicalJson.ComputeHash(pipeline);
        var payload = new CalculationFingerprintPayload(
            request.CalculationId,
            request.ContentRevision,
            pipeline.PipelineId,
            pipelineFingerprint,
            request.Channel,
            request.BaseValue,
            current,
            request.Tags.OrderBy(item => item, StringComparer.Ordinal).ToImmutableArray(),
            request.Variables.ToImmutableSortedDictionary(StringComparer.Ordinal),
            request.BaseTrace.ToImmutableArray(),
            traceArray);
        return Result<CalculationResult>.Success(new CalculationResult
        {
            CalculationId = request.CalculationId,
            ContentRevision = request.ContentRevision,
            PipelineId = pipeline.PipelineId,
            PipelineFingerprint = pipelineFingerprint,
            Channel = request.Channel,
            BaseValue = request.BaseValue,
            Value = current,
            BaseTrace = request.BaseTrace,
            Buckets = traceArray,
            Tags = request.Tags.OrderBy(item => item, StringComparer.Ordinal).ToImmutableArray(),
            Variables = request.Variables.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Fingerprint = payload.Compute()
        });
    }

    public static Result ValidateDefinition(CalculationPipelineDefinition pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (string.IsNullOrWhiteSpace(pipeline.PipelineId))
            return Result.Failure("PipelineId is required");
        if (string.IsNullOrWhiteSpace(pipeline.Channel))
            return Result.Failure($"Pipeline {pipeline.PipelineId} requires a channel");
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
        if (pipeline.Buckets.Any(bucket => !Enum.IsDefined(bucket.Operation) ||
                !Enum.IsDefined(bucket.Rounding) || !Enum.IsDefined(bucket.MidpointRounding) ||
                !Enum.IsDefined(bucket.SetConflict)))
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains an unsupported bucket policy");
        if (pipeline.Buckets.Any(bucket => bucket.Minimum is { } min && !IsFinite(min) ||
                bucket.Maximum is { } max && !IsFinite(max)))
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains non-finite bounds");
        foreach (var bucket in pipeline.Buckets)
        {
            if (bucket.Operation == CalculationBucketOperation.Formula && string.IsNullOrWhiteSpace(bucket.Formula))
                return Result.Failure($"Formula bucket {bucket.BucketId} requires formula");
            if (bucket.Operation != CalculationBucketOperation.Formula && !string.IsNullOrWhiteSpace(bucket.Formula))
                return Result.Failure($"Bucket {bucket.BucketId} declares formula without Formula operation");
        }

        var buckets = pipeline.Buckets.Select(bucket => bucket.BucketId).ToHashSet(StringComparer.Ordinal);
        var duplicateBinding = pipeline.ResourceInfluenceBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.BindingId))
            .GroupBy(binding => binding.BindingId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBinding != null)
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains duplicate resource influence binding {duplicateBinding.Key}");
        foreach (var binding in pipeline.ResourceInfluenceBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.BindingId) || string.IsNullOrWhiteSpace(binding.ResourceId) ||
                string.IsNullOrWhiteSpace(binding.Channel) || string.IsNullOrWhiteSpace(binding.Bucket))
                return Result.Failure($"Pipeline {pipeline.PipelineId} contains an incomplete resource influence binding");
            if (!Enum.IsDefined(binding.Scope) || !Enum.IsDefined(binding.Field) || !Enum.IsDefined(binding.MissingResource))
                return Result.Failure($"Resource influence binding {binding.BindingId} has an invalid scope or field");
            if (!string.Equals(binding.Channel, pipeline.Channel, StringComparison.Ordinal))
                return Result.Failure($"Resource influence binding {binding.BindingId} targets another channel");
            if (!buckets.Contains(binding.Bucket))
                return Result.Failure($"Resource influence binding {binding.BindingId} targets unknown bucket {binding.Bucket}");
            if (!IsFinite(binding.Scale) || !IsFinite(binding.Offset))
                return Result.Failure($"Resource influence binding {binding.BindingId} must be finite");
            if (binding.RequiredTags.Any(string.IsNullOrWhiteSpace) || binding.ExcludedTags.Any(string.IsNullOrWhiteSpace))
                return Result.Failure($"Resource influence binding {binding.BindingId} contains an empty tag");
            if (binding.Settlement != null)
            {
                if (!Enum.IsDefined(binding.Settlement.Operation) || !Enum.IsDefined(binding.Settlement.Field))
                    return Result.Failure($"Resource influence binding {binding.BindingId} has an invalid settlement");
                if (!IsFinite(binding.Settlement.Scale) || !IsFinite(binding.Settlement.Offset))
                    return Result.Failure($"Resource influence binding {binding.BindingId} settlement must be finite");
            }
        }
        var duplicateStatBinding = pipeline.StatInfluenceBindings
            .Where(binding => !string.IsNullOrWhiteSpace(binding.BindingId))
            .GroupBy(binding => binding.BindingId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateStatBinding != null)
            return Result.Failure($"Pipeline {pipeline.PipelineId} contains duplicate stat influence binding {duplicateStatBinding.Key}");
        foreach (var binding in pipeline.StatInfluenceBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.BindingId) || string.IsNullOrWhiteSpace(binding.ComponentId) ||
                string.IsNullOrWhiteSpace(binding.ValueId) || string.IsNullOrWhiteSpace(binding.Channel) ||
                string.IsNullOrWhiteSpace(binding.Bucket))
                return Result.Failure($"Pipeline {pipeline.PipelineId} contains an incomplete stat influence binding");
            if (!Enum.IsDefined(binding.Scope) || !Enum.IsDefined(binding.MissingValue))
                return Result.Failure($"Stat influence binding {binding.BindingId} has an invalid scope or missing-value policy");
            if (!string.Equals(binding.Channel, pipeline.Channel, StringComparison.Ordinal))
                return Result.Failure($"Stat influence binding {binding.BindingId} targets another channel");
            if (!buckets.Contains(binding.Bucket))
                return Result.Failure($"Stat influence binding {binding.BindingId} targets unknown bucket {binding.Bucket}");
            if (!IsFinite(binding.Scale) || !IsFinite(binding.Offset))
                return Result.Failure($"Stat influence binding {binding.BindingId} must be finite");
            if (binding.RequiredTags.Any(string.IsNullOrWhiteSpace) || binding.ExcludedTags.Any(string.IsNullOrWhiteSpace))
                return Result.Failure($"Stat influence binding {binding.BindingId} contains an empty tag");
        }
        return Result.Success();
    }

    private static Result Validate(
        CalculationRequest request,
        CalculationPipelineDefinition pipeline)
    {
        var definition = ValidateDefinition(pipeline);
        if (definition.IsFailure) return definition;
        if (string.IsNullOrWhiteSpace(request.CalculationId))
            return Result.Failure("CalculationId is required");
        if (string.IsNullOrWhiteSpace(request.Channel))
            return Result.Failure("Calculation channel is required");
        if (!string.Equals(request.Channel, pipeline.Channel, StringComparison.Ordinal))
            return Result.Failure(
                $"Calculation channel {request.Channel} does not match pipeline channel {pipeline.Channel}");
        if (!IsFinite(request.BaseValue))
            return Result.Failure("Calculation base value must be finite");
        var buckets = pipeline.Buckets.Select(bucket => bucket.BucketId).ToHashSet(StringComparer.Ordinal);
        if (request.Tags.Any(string.IsNullOrWhiteSpace))
            return Result.Failure("Calculation tags cannot contain an empty value");
        if (request.Variables.Any(item => string.IsNullOrWhiteSpace(item.Key) || !IsFinite(item.Value)))
            return Result.Failure("Calculation variables require nonempty keys and finite values");
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

    private Result<BucketEvaluation> EvaluateBucket(
        CalculationRequest request,
        CalculationBucketDefinition bucket,
        float current,
        ImmutableArray<CalculationInfluence> influences)
    {
        if (bucket.Operation == CalculationBucketOperation.Formula)
        {
            if (formulas == null)
                return Result<BucketEvaluation>.Failure($"Formula bucket {bucket.BucketId} requires a formula evaluator");
            var variables = request.Variables.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            variables["calculation.base"] = request.BaseValue;
            variables["bucket.input"] = current;
            variables["bucket.contributions.count"] = influences.Length;
            variables["bucket.contributions.sum"] = influences.Sum(item => item.Value);
            variables["bucket.contributions.minimum"] = influences.IsEmpty ? 0 : influences.Min(item => item.Value);
            variables["bucket.contributions.maximum"] = influences.IsEmpty ? 0 : influences.Max(item => item.Value);
            var result = !string.IsNullOrWhiteSpace(request.ContentRevision) && formulas is IRevisionedRuntimeFormulaEvaluator revisioned
                ? revisioned.EvaluateAtRevision(bucket.Formula!, request.ContentRevision, variables, current)
                : formulas.Evaluate(bucket.Formula!, variables, current);
            if (result.IsFailure) return Result<BucketEvaluation>.Failure(result.Error);
            return Result<BucketEvaluation>.Success(new(result.Value,
                influences.Select(item => Trace(item, current, true, item.Value, result.Value)).ToImmutableArray()));
        }

        var traces = ImmutableArray.CreateBuilder<CalculationContributionTrace>();
        if (bucket.Operation == CalculationBucketOperation.Set)
        {
            if (influences.IsEmpty) return Result<BucketEvaluation>.Success(new(current, []));
            var selected = bucket.SetConflict == SetConflictPolicy.LowestPriorityWins
                ? influences.OrderBy(item => item.Priority)
                    .ThenBy(item => item.OrderKey, StringComparer.Ordinal)
                    .ThenBy(item => item.InfluenceId, StringComparer.Ordinal)
                    .ThenBy(item => item.SourceId, StringComparer.Ordinal).First()
                : influences[0];
            foreach (var influence in influences)
                traces.Add(Trace(influence, current, ReferenceEquals(influence, selected),
                    ReferenceEquals(influence, selected) ? influence.Value : null,
                    ReferenceEquals(influence, selected) ? influence.Value : current));
            return Result<BucketEvaluation>.Success(new(selected.Value, traces.ToImmutable()));
        }

        var value = current;
        var percentBase = current;
        foreach (var influence in influences)
        {
            var before = value;
            float effective;
            switch (bucket.Operation)
            {
                case CalculationBucketOperation.Add:
                    effective = influence.Value;
                    value += effective;
                    break;
                case CalculationBucketOperation.AddPercent:
                    effective = influence.Value;
                    value += percentBase * effective;
                    break;
                case CalculationBucketOperation.Multiply:
                    effective = influence.Value;
                    value *= effective;
                    break;
                case CalculationBucketOperation.Minimum:
                    effective = influence.Value;
                    value = MathF.Min(value, effective);
                    break;
                case CalculationBucketOperation.Maximum:
                    effective = influence.Value;
                    value = MathF.Max(value, effective);
                    break;
                case CalculationBucketOperation.ConsumeCapacity:
                    if (influence.Value < 0)
                        return Result<BucketEvaluation>.Failure(
                            $"Bucket {bucket.BucketId} received negative capacity from {influence.InfluenceId}");
                    effective = MathF.Min(MathF.Max(value, 0), influence.Value);
                    value -= effective;
                    break;
                default:
                    return Result<BucketEvaluation>.Failure($"Unsupported bucket operation: {bucket.Operation}");
            }
            traces.Add(Trace(influence, before, true, effective, value));
        }
        return Result<BucketEvaluation>.Success(new(value, traces.ToImmutable()));
    }

    private static CalculationContributionTrace Trace(
        CalculationInfluence influence, float input, bool applied, float? effective, float output) => new()
    {
        InfluenceId = influence.InfluenceId,
        SourceKind = influence.SourceKind,
        SourceId = influence.SourceId,
        Value = influence.Value,
        Priority = influence.Priority,
        OrderKey = influence.OrderKey,
        Applied = applied,
        Input = input,
        EffectiveValue = effective,
        Output = output
    };

    private static float ApplyRounding(
        float value,
        CalculationRounding rounding,
        CalculationMidpointRounding midpoint) => rounding switch
    {
        CalculationRounding.Floor => MathF.Floor(value),
        CalculationRounding.Ceiling => MathF.Ceiling(value),
        CalculationRounding.Round => MathF.Round(value, midpoint switch
        {
            CalculationMidpointRounding.ToEven => MidpointRounding.ToEven,
            CalculationMidpointRounding.AwayFromZero => MidpointRounding.AwayFromZero,
            CalculationMidpointRounding.ToZero => MidpointRounding.ToZero,
            CalculationMidpointRounding.ToNegativeInfinity => MidpointRounding.ToNegativeInfinity,
            CalculationMidpointRounding.ToPositiveInfinity => MidpointRounding.ToPositiveInfinity,
            _ => MidpointRounding.ToEven
        }),
        _ => value
    };

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private sealed record BucketEvaluation(
        float Value,
        ImmutableArray<CalculationContributionTrace> Contributions);
}
