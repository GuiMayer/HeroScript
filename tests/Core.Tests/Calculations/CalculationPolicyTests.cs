using Core.Calculations;
using Core.Tests.Effects;
using Xunit;

namespace Core.Tests.Calculations;

public sealed class CalculationPolicyTests
{
    [Theory]
    [InlineData(SetConflictPolicy.HighestPriorityWins, 9)]
    [InlineData(SetConflictPolicy.LowestPriorityWins, 2)]
    public void SetUsesConfiguredPriorityPolicy(SetConflictPolicy policy, float expected)
    {
        var pipeline = Pipeline() with { Buckets = [new() { BucketId = "value", Operation = CalculationBucketOperation.Set, SetConflict = policy }] };
        var result = new CalculationEngine().Calculate(Request(), pipeline);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, result.Value.Value);
    }

    [Fact]
    public void SetCanRejectMultipleContributions()
    {
        var pipeline = Pipeline() with { Buckets = [new() { BucketId = "value", Operation = CalculationBucketOperation.Set, SetConflict = SetConflictPolicy.ErrorOnMultiple }] };
        Assert.True(new CalculationEngine().Calculate(Request(), pipeline).IsFailure);
    }

    [Fact]
    public void NonFiniteIntermediateAndInvalidPoliciesFailWithoutHashException()
    {
        var engine = new CalculationEngine();
        Assert.True(engine.Calculate(Request() with { BaseValue = float.MaxValue }, Pipeline() with
        { Buckets = [new() { BucketId = "value", Operation = CalculationBucketOperation.Multiply }] }).IsFailure);
        Assert.True(engine.Calculate(Request(), Pipeline() with
        { Buckets = [new() { BucketId = "value", Minimum = float.NaN }] }).IsFailure);
        Assert.True(engine.Calculate(Request(), Pipeline() with
        { Buckets = [new() { BucketId = "value", Operation = (CalculationBucketOperation)999 }] }).IsFailure);
    }

    [Theory]
    [InlineData(MissingResourcePolicy.Ignore, 0, false)]
    [InlineData(MissingResourcePolicy.Zero, 1, false)]
    [InlineData(MissingResourcePolicy.Error, 0, true)]
    public void MissingResourceUsesExplicitPolicy(MissingResourcePolicy policy, int count, bool failure)
    {
        var result = new EntityResourceInfluenceProvider().Collect(new()
        {
            Actor = GameplayOwnershipTests.State().Hero,
            Pipeline = Pipeline() with { ResourceInfluenceBindings = [new()
            { BindingId = "missing", ResourceId = "other", Channel = "amount", Bucket = "value", MissingResource = policy, Offset = 4 }] }
        });
        Assert.Equal(failure, result.IsFailure);
        if (!failure)
        {
            Assert.Equal(count, result.Value.Count);
            if (count > 0) Assert.Equal(4, result.Value[0].Value);
        }
    }

    private static CalculationPipelineDefinition Pipeline() => new()
    { PipelineId = "test", Channel = "amount", Buckets = [new() { BucketId = "value" }] };
    private static CalculationRequest Request() => new()
    {
        CalculationId = "test", Channel = "amount", BaseValue = 1,
        Influences = [new() { InfluenceId = "low", SourceId = "a", Channel = "amount", Bucket = "value", Priority = 1, Value = 2 },
            new() { InfluenceId = "high", SourceId = "b", Channel = "amount", Bucket = "value", Priority = 10, Value = 9 }]
    };
}
