using Core.Calculations;
using Core.Determinism;
using Core.Effects;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Effects;

public sealed class ScopedEffectInputsTests
{
    [Theory]
    [InlineData(EffectExecutionScope.OncePerAction, 1)]
    [InlineData(EffectExecutionScope.OncePerParentProc, 3)]
    [InlineData(EffectExecutionScope.EveryInvocation, 3)]
    public void TriggerScopeIsBoundToLogicalNodeRatherThanRuntimeRepeatPath(EffectExecutionScope scope, int applications)
    {
        var child = EffectTransactionTests.Resource(EffectType.DAMAGE, 1) with { ExecutionScope = scope };
        var parent = EffectTransactionTests.Resource(EffectType.DAMAGE, 0) with { Repeat = 3, ChainedEffects = [child] };
        var request = EffectTransactionTests.Request(parent);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(10 - applications, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(scope == EffectExecutionScope.OncePerAction ? 2 : 0, result.Value.Steps.Count(step => step.SkipReason == "scope_already_attempted"));
    }

    [Theory]
    [InlineData(EffectChanceScope.PerAction, 1)]
    [InlineData(EffectChanceScope.PerProc, 3)]
    public void ChildChanceScopeReusesActionOrParentProcDraws(EffectChanceScope scope, int draws)
    {
        var child = EffectTransactionTests.Resource(EffectType.DAMAGE, 0) with { Chance = .65f, ChanceScope = scope };
        var parent = EffectTransactionTests.Resource(EffectType.DAMAGE, 0) with { Repeat = 3, ChainedEffects = [child] };
        var request = EffectTransactionTests.Request(parent);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var context = request.Combat.Determinism;
        for (var i = 0; i < draws; i++) context = context.DrawDouble().Context;
        Assert.Equal(context.RandomState, result.Value.State.Determinism.RandomState);
    }

    [Theory]
    [InlineData(EffectRandomScope.Action, 1)]
    [InlineData(EffectRandomScope.ParentProc, 3)]
    [InlineData(EffectRandomScope.Impact, 3)]
    public void CriticalIsOnlyAnAuditedRandomInputConsumedByNumericResolution(EffectRandomScope scope, int draws)
    {
        var effect = EffectTransactionTests.Resource(EffectType.DAMAGE, 1) with
        { Repeat = 3, FormulaValue = "rolls.critical.success", RandomInputs = [new() { InputId = "critical", Chance = .5f, Scope = scope }] };
        var request = EffectTransactionTests.Request(effect);
        var results = Enumerable.Range(0, 10).Select(_ => EffectTransactionTests.Executor().Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(results.Select(result => CanonicalJson.ComputeHash(result.Value)).Distinct());
        var result = results[0].Value;
        var context = request.Combat.Determinism;
        for (var i = 0; i < draws; i++) context = context.DrawDouble().Context;
        Assert.Equal(context.RandomState, result.State.Determinism.RandomState);
        Assert.All(result.Steps, step => Assert.Equal(step.RandomInputs[0].Success ? 2 : 1, step.Calculation!.Value));
        if (scope == EffectRandomScope.Action) Assert.Single(result.Steps.Select(step => step.RandomInputs[0].ScopeId).Distinct());
    }

    [Fact]
    public void ActionCriticalFeedsTheSourceBudgetOnceInsteadOfAddingItsBonusToEachHit()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var effect = EffectSequenceBudgetTests.Damage(10, 3) with
        { RandomInputs = [new() { InputId = "critical", Scope = EffectRandomScope.Action, Chance = 1 }] };
        var request = EffectTransactionTests.Request(effect) with { Run = fixture.Run, Card = new()
        { Components = [new CardInfluenceComponentDefinition { ComponentId = "crit", Channel = "magnitude", Bucket = "flat", Formula = "rolls.critical.success" }] } };
        var result = fixture.Executor.Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(11, result.Value.Steps[0].SequenceBudgets[0].Capture.Value);
        Assert.Equal(new[] { 4f, 4f, 3f }, result.Value.Steps.Select(step => step.ImpactShares[0].Share.Quantity.Value));
        Assert.Equal(request.Combat.Determinism.RandomState, result.Value.State.Determinism.RandomState);
    }

    [Fact]
    public void BeforeImpactEffectsPrecedeCalculationAndKeepStepHashChainContiguous()
    {
        var before = EffectTransactionTests.Resource(EffectType.DAMAGE, 2) with { ChildTiming = EffectChildTiming.BeforeParentImpact };
        var parent = EffectTransactionTests.Resource(EffectType.DAMAGE, null) with
        { FormulaValue = "target.resources.focus.current", Chance = .99f, ChainedEffects = [before] };
        var request = EffectTransactionTests.Request(parent);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(2, result.Value.Steps.Length);
        Assert.Equal(2, result.Value.Steps[0].Calculation!.Value);
        Assert.Equal(8, result.Value.Steps[1].Calculation!.Value);
        Assert.Equal(CanonicalJson.ComputeHash(request.Combat), result.Value.Steps[0].StateBeforeHash);
        Assert.Equal(result.Value.Steps[0].StateAfterHash, result.Value.Steps[1].StateBeforeHash);
        Assert.Equal(0, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void ForgedRandomNamespaceAndInvalidScopedInputDefinitionsAreRejected()
    {
        var request = EffectTransactionTests.Request(EffectTransactionTests.Resource(EffectType.DAMAGE, 1));
        Assert.True(EffectTransactionTests.Executor().Execute(request with { Variables = new Dictionary<string, float> { ["rolls.critical.success"] = 1 } }).IsFailure);
        Assert.NotEmpty(EffectDefinitionValidator.Validate([request.Trigger.Effects[0] with
            { RandomInputs = [new() { InputId = "x", Chance = float.NaN }] }]));
        Assert.True(EffectTransactionTests.Executor().Execute(request with { Trigger = request.Trigger with
            { Effects = [request.Trigger.Effects[0] with { ExecutionScope = EffectExecutionScope.OncePerParentProc }] } }).IsFailure);
    }

    [Fact]
    public void ZeroStackSlotsDoNotInvokeBeforeImpactTriggers()
    {
        var fixture = EffectSequenceBudgetTests.Fixture();
        var parent = EffectSequenceBudgetTests.Stacks(EffectNumericParameter.StatusStacks, 1, 4) with
        { ChainedEffects = [EffectSequenceBudgetTests.Damage(1, 1) with { ChildTiming = EffectChildTiming.BeforeParentImpact }] };
        var result = fixture.Executor.Execute(EffectTransactionTests.Request(parent) with { Run = fixture.Run });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(9, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Equal(5, result.Value.Steps.Length);
    }

    [Fact]
    public void ExplicitGroupsShareCriticalChanceAndTriggerAcrossDistinctComponents()
    {
        var first = EffectTransactionTests.Resource(EffectType.DAMAGE, 0) with { RandomInputs = [new()
            { InputId = "crit", GroupId = "action_critical", Scope = EffectRandomScope.Action, Chance = .5f }] };
        var request = EffectTransactionTests.Request(first, first);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(request.Combat.Determinism.DrawDouble().Context.RandomState, result.Value.State.Determinism.RandomState);
        Assert.Equal(result.Value.Steps[0].RandomInputs.ToArray(), result.Value.Steps[1].RandomInputs.ToArray());
        var once = first with { RandomInputs = [], ExecutionScope = EffectExecutionScope.OncePerAction, ExecutionGroupId = "single_proc" };
        var onceResult = EffectTransactionTests.Executor().Execute(EffectTransactionTests.Request(once, once));
        Assert.True(onceResult.IsSuccess);
        Assert.Single(onceResult.Value.Records);
        Assert.Equal("scope_already_attempted", onceResult.Value.Steps[1].SkipReason);
        Assert.NotEmpty(EffectDefinitionValidator.Validate([first, first with { RandomInputs = [first.RandomInputs[0] with { Chance = .25f }] }]));
    }

    [Theory]
    [InlineData(EffectTargetLossPolicy.Skip, 4)]
    [InlineData(EffectTargetLossPolicy.StopRepeat, 2)]
    public void BeforeImpactDefeatUsesParentLossPolicyRatherThanApplyingToADeadTarget(EffectTargetLossPolicy policy, int steps)
    {
        var parent = EffectTransactionTests.Resource(EffectType.DAMAGE, 1) with { Repeat = 3, TargetLoss = new() { Policy = policy },
            ChainedEffects = [EffectTransactionTests.Resource(EffectType.DAMAGE, 10) with { ChildTiming = EffectChildTiming.BeforeParentImpact }] };
        var request = EffectTransactionTests.Request(parent);
        request = request with { Combat = EffectSequenceBudgetTests.DefeatAtZero(request.Combat, "enemy", 2) };
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(steps, result.Value.Steps.Length);
        Assert.Single(result.Value.Records);
        Assert.All(result.Value.Steps.Skip(1), step => Assert.False(step.Applied));
        Assert.Equal(result.Value.Steps[0].StateAfterHash, result.Value.Steps[1].StateBeforeHash);
    }
}
