using System.Collections.Immutable;

namespace Core.Calculations;

/// <summary>Advisory authoring information only. Never changes numeric policy or hashes.</summary>
public static class CalculationAuthoringDiagnostics
{
    public static ImmutableArray<string> Inspect(CalculationPipelineDefinition pipeline)
    {
        var warnings = ImmutableArray.CreateBuilder<string>();
        var buckets = pipeline.Buckets.OrderBy(bucket => bucket.Order).ToArray();
        for (var index = 0; index < buckets.Length; index++)
        {
            var bucket = buckets[index];
            if (index > 0 && bucket.Operation == CalculationBucketOperation.AddPercent &&
                buckets[index - 1].Operation == CalculationBucketOperation.AddPercent)
                warnings.Add($"CALC_PERCENT_GROUPS: {pipeline.PipelineId}/{buckets[index - 1].BucketId}/{bucket.BucketId}: separate percent buckets compound; use one bucket if contributions should add");
            if (index < buckets.Length - 1 && bucket.Rounding != CalculationRounding.None)
                warnings.Add($"CALC_INTERMEDIATE_ROUNDING: {pipeline.PipelineId}/{bucket.BucketId}: rounding occurs before later buckets");
        }
        return warnings.ToImmutable();
    }
}
