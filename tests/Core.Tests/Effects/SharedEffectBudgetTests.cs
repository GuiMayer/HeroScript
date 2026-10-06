using Core.Effects;
using Core.Calculations;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Effects;

public sealed class SharedEffectBudgetTests
{
    [Theory]
    [InlineData(1, 4, CalculationRemainderAllocation.Earliest, "1,0,0,0")]
    [InlineData(1, 4, CalculationRemainderAllocation.Latest, "0,0,0,1")]
    [InlineData(10, 3, CalculationRemainderAllocation.Earliest, "4,3,3")]
    public void ResidualBudgetIsSharedAcrossParentHits(int stacks, int repeat, CalculationRemainderAllocation bias, string expected)
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var child = EffectSequenceBudgetTests.Stacks(EffectNumericParameter.StatusStacks, stacks, 1, bias);
        child = child with { Parameters = [child.Parameters[0] with { Distribution = child.Parameters[0].Distribution! with
            { Scope = EffectDistributionScope.ParentSequence } }] };
        var parent = EffectSequenceBudgetTests.Damage(0, repeat) with { ChainedEffects = [child] };
        var request = EffectTransactionTests.Request(parent) with { Run = fixture.Run };
        var hash = CanonicalJson.ComputeHash(request);
        var runs = Enumerable.Range(0, 10).Select(_ => fixture.Executor.Execute(request)).ToArray();
        Assert.All(runs, run => Assert.True(run.IsSuccess, run.IsFailure ? run.Error : null));
        Assert.Single(runs.Select(run => CanonicalJson.ComputeHash(run.Value)).Distinct());
        var result = runs[0].Value;
        var residuals = result.Steps.Where(step => step.ImpactShares.Any(share => share.Parameter == EffectNumericParameter.StatusStacks)).ToArray();
        Assert.Equal(expected, string.Join(",", residuals.Select(step => step.ImpactShares[0].Share.Quantity.Value)));
        Assert.Single(residuals.SelectMany(step => step.SequenceBudgets));
        Assert.Equal(stacks, result.State.StatusEffects.GetValueOrDefault("enemy", []).Sum(status => status.Stacks));
        Assert.Equal(hash, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void SiblingResidualsHaveIndependentBudgetsAndALateFailureRollsBackBoth()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var child = EffectSequenceBudgetTests.Stacks(EffectNumericParameter.StatusStacks, 1, 1);
        child = child with { Parameters = [child.Parameters[0] with { Distribution = child.Parameters[0].Distribution! with
            { Scope = EffectDistributionScope.ParentSequence } }] };
        var parent = EffectSequenceBudgetTests.Damage(0, 3) with { ChainedEffects = [child, child] };
        var request = EffectTransactionTests.Request(parent) with { Run = fixture.Run };
        var success = fixture.Executor.Execute(request);
        Assert.True(success.IsSuccess, success.IsFailure ? success.Error : null);
        Assert.Equal(2, success.Value.State.StatusEffects["enemy"].Sum(status => status.Stacks));
        Assert.Equal(3, success.Value.Steps.SelectMany(step => step.SequenceBudgets).Count());
        var invalid = EffectSequenceBudgetTests.Damage(1, 1) with { TargetResource = "missing" };
        var before = CanonicalJson.ComputeHash(request);
        Assert.True(fixture.Executor.Execute(request with { Trigger = request.Trigger with { Effects = [parent, invalid] } }).IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public void RootCannotBorrowAMissingParentAndSharedChildCannotRepeatItsAllocatedSlot()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var child = EffectSequenceBudgetTests.Stacks(EffectNumericParameter.StatusStacks, 1, 1);
        child = child with { Parameters = [child.Parameters[0] with { Distribution = child.Parameters[0].Distribution! with
            { Scope = EffectDistributionScope.ParentSequence } }] };
        Assert.True(fixture.Executor.Execute(EffectTransactionTests.Request(child) with { Run = fixture.Run }).IsFailure);
        Assert.NotEmpty(EffectDefinitionValidator.Validate([child with { Repeat = 2 }]));
    }
}
