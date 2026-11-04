using Core.Calculations;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class CalculationAuthoringDiagnosticsTests
{
    [Fact]
    public void AdvisoriesExplainCompoundingWithoutChangingExecutablePolicy()
    {
        var pipeline = new CalculationPipelineDefinition { PipelineId = "custom", Channel = "custom", Buckets = [
            new() { BucketId = "first", Order = 10, Operation = CalculationBucketOperation.AddPercent, Rounding = CalculationRounding.Floor },
            new() { BucketId = "second", Order = 20, Operation = CalculationBucketOperation.AddPercent }] };
        var before = CanonicalJson.ComputeHash(pipeline);
        Assert.True(CalculationEngine.ValidateDefinition(pipeline).IsSuccess);
        var warnings = CalculationAuthoringDiagnostics.Inspect(pipeline);
        Assert.Contains(warnings, message => message.StartsWith("CALC_PERCENT_GROUPS:"));
        Assert.Contains(warnings, message => message.StartsWith("CALC_INTERMEDIATE_ROUNDING:"));
        Assert.Equal(before, CanonicalJson.ComputeHash(pipeline));
        Assert.Equal(warnings.ToArray(), CalculationAuthoringDiagnostics.Inspect(pipeline).ToArray());
    }
}
