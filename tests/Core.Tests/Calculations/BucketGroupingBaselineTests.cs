using Core.Calculations;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class BucketGroupingBaselineTests
{
    [Theory]
    [InlineData(false, 145f)]
    [InlineData(true, 150f)]
    public void PercentGroups_DefineWhetherHeatCompounds(bool separate, float expected)
    {
        var pipeline = new CalculationPipelineDefinition
        {
            PipelineId = "baseline", Channel = "effect_amount", UnitId = "points",
            Buckets = separate
                ? [new() { BucketId = "increased", Order = 10, Operation = CalculationBucketOperation.AddPercent },
                   new() { BucketId = "heat", Order = 20, Operation = CalculationBucketOperation.AddPercent }]
                : [new() { BucketId = "increased", Order = 10, Operation = CalculationBucketOperation.AddPercent }]
        };
        var request = new CalculationRequest
        {
            CalculationId = "baseline", Channel = "effect_amount", UnitId = "points", BaseValue = 100,
            Influences = [
                new() { InfluenceId = "attribute", SourceId = "actor", Channel = "effect_amount", Bucket = "increased", Value = .2f },
                new() { InfluenceId = "heat", SourceId = "actor", Channel = "effect_amount", Bucket = separate ? "heat" : "increased", Value = .25f }
            ]
        };
        var engine = new CalculationEngine();
        var first = engine.Calculate(request, pipeline);
        var second = engine.Calculate(request, pipeline);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(expected, first.Value.Value, 4);
        Assert.Equal(first.Value.Fingerprint, second.Value.Fingerprint);
        Assert.Equal(100, request.BaseValue);
        Assert.Equal(2, request.Influences.Count);
    }
}
