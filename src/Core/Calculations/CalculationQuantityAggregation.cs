using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Calculations;

public sealed record WeightedCalculationQuantity(string ContributionId, CalculationQuantity Quantity, int Weight);
public sealed record CalculationQuantityAggregate(CalculationQuantity Quantity, CalculationResult Trace);

/// <summary>Pure weighted numeric aggregation; orchestration never sums gameplay magnitudes.</summary>
public static class CalculationQuantityAggregation
{
    public static Result<CalculationQuantityAggregate> Sum(ICalculationEngine engine, string id,
        IReadOnlyList<WeightedCalculationQuantity> contributions)
    {
        if (contributions.Count is < 1 or > 4096 || contributions.Any(item => item.Weight < 1 ||
            string.IsNullOrWhiteSpace(item.ContributionId)) ||
            contributions.Select(item => item.ContributionId).Distinct(StringComparer.Ordinal).Count() != contributions.Count)
            return Result<CalculationQuantityAggregate>.Failure("Invalid numeric aggregation contributions");
        var ordered = contributions.OrderBy(item => item.ContributionId, StringComparer.Ordinal).ToImmutableArray();
        var first = ordered[0].Quantity;
        if (ordered.Any(item => item.Quantity.UnitId != first.UnitId || item.Quantity.ContentRevision != first.ContentRevision ||
            !float.IsFinite(item.Quantity.Value) || string.IsNullOrWhiteSpace(item.Quantity.CalculationFingerprint)))
            return Result<CalculationQuantityAggregate>.Failure("Cannot aggregate incompatible quantity units, revisions or provenance");
        var receipts = ordered.SelectMany(item => item.Quantity.IncorporatedStages)
            .GroupBy(receipt => (receipt.StageId, receipt.Scope, receipt.ContextId)).ToArray();
        if (receipts.Any(group => group.Select(receipt => (receipt.PipelineId, receipt.PipelineFingerprint)).Distinct().Count() != 1))
            return Result<CalculationQuantityAggregate>.Failure("Cannot aggregate incompatible stage definitions");
        var result = engine.Calculate(new()
        {
            CalculationId = id, ContentRevision = first.ContentRevision, UnitId = first.UnitId, Channel = "quantity_sum",
            CaptureOnly = true, Influences = ordered.Select(item => new CalculationInfluence
            {
                InfluenceId = item.ContributionId, SourceId = item.Quantity.CalculationFingerprint,
                Channel = "quantity_sum", Bucket = "sum", Value = item.Quantity.Value * item.Weight
            }).ToArray()
        }, new() { PipelineId = "__weighted_quantity_sum__", Channel = "quantity_sum", UnitId = first.UnitId,
            Buckets = [new() { BucketId = "sum" }] });
        if (result.IsFailure) return Result<CalculationQuantityAggregate>.Failure(result.Error);
        var quantity = result.Value.Quantity with
        {
            IncorporatedStages = receipts.Select(group => group.First()).OrderBy(receipt => receipt.StageId, StringComparer.Ordinal)
                .ThenBy(receipt => receipt.Scope).ThenBy(receipt => receipt.ContextId, StringComparer.Ordinal).ToImmutableArray(),
            CalculationFingerprint = CanonicalJson.ComputeHash(new { result.Value.Fingerprint, Contributions = ordered })
        };
        return Result<CalculationQuantityAggregate>.Success(new(quantity, result.Value));
    }
}
