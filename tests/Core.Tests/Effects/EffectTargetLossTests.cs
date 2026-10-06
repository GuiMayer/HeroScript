using Core.Combat;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Xunit;

namespace Core.Tests.Effects;

public sealed class EffectTargetLossTests
{
    [Theory]
    [InlineData(EffectTargetLossPolicy.Skip)]
    [InlineData(EffectTargetLossPolicy.StopRepeat)]
    public void FirstLethalHitDoesNotCancelConfiguredRepeatedAction(EffectTargetLossPolicy policy)
    {
        var request = Request(Hit(15) with { Repeat = 2, TargetLoss = new() { Policy = policy } });
        var before = CanonicalJson.ComputeHash(request.Combat);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, result.Value.State.GetActor("enemy")!.GetResource("focus")!.Current);
        Assert.Single(result.Value.Records);
        Assert.Single(result.Value.Calculations);
        Assert.Contains(result.Value.Steps, step => !step.Applied && step.TargetLossPolicy == policy);
        Assert.Equal(before, CanonicalJson.ComputeHash(request.Combat));
    }

    [Fact]
    public void LastHitCanKillWithoutExtraFailedRepeat()
    {
        var result = EffectTransactionTests.Executor().Execute(Request(Hit(5) with { Repeat = 2 }));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.False(result.Value.State.GetActor("enemy")!.IsAlive);
        Assert.Equal(2, result.Value.Records.Count);
    }

    [Fact]
    public void StatusAfterLethalDamageIsSkippedWithoutConsumingChanceOrRunningChildren()
    {
        var request = Request(Hit(15), new EffectDefinition
        {
            EffectId = "burn", Type = EffectType.APPLY_STATUS, StatusId = "burning",
            Chance = .5f, TargetLoss = new() { Policy = EffectTargetLossPolicy.Skip },
            ChainedEffects = [Hit(1) with { Target = EffectTarget.SELF }]
        });
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.Records);
        Assert.Empty(result.Value.State.StatusEffects);
        Assert.Equal(request.Combat.Determinism, result.Value.State.Determinism);
        Assert.Equal("target_defeated", result.Value.Steps[1].SkipReason);
    }

    [Fact]
    public void ChildAfterLethalParentUsesTheSameLossPolicy()
    {
        var result = EffectTransactionTests.Executor().Execute(Request(Hit(15) with
        { ChainedEffects = [Hit(2) with { TargetLoss = new() { Policy = EffectTargetLossPolicy.Skip } }] }));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.Records);
        Assert.Equal("target_defeated", result.Value.Steps[1].SkipReason);
    }

    [Theory]
    [InlineData(EffectTargetLossPolicy.Fail)]
    [InlineData(EffectTargetLossPolicy.Skip)]
    [InlineData(EffectTargetLossPolicy.StopRepeat)]
    [InlineData(EffectTargetLossPolicy.Retarget)]
    public void LossPolicyNeverAcceptsInvalidEntrySelection(EffectTargetLossPolicy policy)
    {
        var effect = Hit(1) with { TargetLoss = new() { Policy = policy,
            Retarget = policy == EffectTargetLossPolicy.Retarget ? EffectTarget.RANDOM_ENEMY : null } };
        var request = Request(effect) with { SelectedTargetEntityIds = ["missing"] };
        Assert.True(EffectTransactionTests.Executor().Execute(request).IsFailure);
        var dead = Request(Hit(15));
        var defeated = EffectTransactionTests.Executor().Execute(dead).Value.State;
        Assert.True(EffectTransactionTests.Executor().Execute(Request(effect) with { Combat = defeated }).IsFailure);
        Assert.True(EffectTransactionTests.Executor().Execute(Request(effect) with { SelectedTargetEntityIds = [] }).IsFailure);
    }

    [Fact]
    public void StrictPolicyRollsBackLethalHitAndEarlierRandomDraw()
    {
        var request = Request(Hit(1) with { Chance = .9f }, Hit(15), Hit(1));
        var hash = CanonicalJson.ComputeHash(request.Combat);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsFailure);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request.Combat));
    }

    [Fact]
    public void AutomaticTargetsCanEndWhenLastEnemyDies()
    {
        var result = EffectTransactionTests.Executor().Execute(Request(Hit(15) with
        { Target = EffectTarget.ALL_ENEMIES, Repeat = 3, TargetLoss = new() { Policy = EffectTargetLossPolicy.StopRepeat } }));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.Records);
        Assert.Equal("repeat_stopped", result.Value.Steps[1].SkipReason);
    }

    [Theory]
    [InlineData(EffectTarget.RANDOM_ENEMY)]
    [InlineData(EffectTarget.LOWEST_RESOURCE_ENEMY)]
    [InlineData(EffectTarget.HIGHEST_RESOURCE_ENEMY)]
    public void RetargetIsDeterministicAndEndsWhenNoCandidateRemains(EffectTarget selector)
    {
        var request = Request(Hit(15) with
        {
            Repeat = 4, SelectionResourceId = "focus",
            TargetLoss = new() { Policy = EffectTargetLossPolicy.Retarget, Retarget = selector }
        }, secondEnemy: true);
        var outputs = Enumerable.Range(0, 10).Select(_ => EffectTransactionTests.Executor().Execute(request)).ToArray();
        Assert.All(outputs, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(outputs.Select(result => result.Value.Fingerprint).Distinct());
        Assert.False(outputs[0].Value.State.GetActor("enemy_b")!.IsAlive);
        Assert.Contains(outputs[0].Value.Steps, step => step.Retargeted);
        var steps = outputs[0].Value.Steps;
        for (var index = 1; index < steps.Length; index++)
            Assert.Equal(steps[index - 1].StateAfterHash, steps[index].StateBeforeHash);
    }

    [Fact]
    public void InvalidRetargetContractIsRejectedBeforeExecution()
    {
        var invalid = Hit(1) with
        { TargetLoss = new() { Policy = EffectTargetLossPolicy.Retarget, Retarget = EffectTarget.TARGET } };
        Assert.NotEmpty(EffectDefinitionValidator.Validate([invalid]));
        Assert.True(EffectTransactionTests.Executor().Execute(Request(invalid)).IsFailure);
    }

    [Fact]
    public void SkipRetainsOtherSelectedTargetsAndContinuousTrace()
    {
        var request = Request([Hit(15), Hit(2) with { TargetLoss = new() { Policy = EffectTargetLossPolicy.Skip } }], true);
        request = request with { SelectedTargetEntityIds = ["enemy", "enemy_b"] };
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(13, result.Value.State.GetActor("enemy_b")!.GetResource("focus")!.Current);
        Assert.Contains(result.Value.Steps, step => step.SkipReason == "target_defeated");
        Assert.Equal(3, result.Value.Records.Count);
        for (var index = 1; index < result.Value.Steps.Length; index++)
            Assert.Equal(result.Value.Steps[index - 1].StateAfterHash, result.Value.Steps[index].StateBeforeHash);
    }

    [Fact]
    public void RealFailureAfterSkippedDeadTargetStillRollsBackWholeAction()
    {
        var request = Request(Hit(15), Hit(1) with { TargetLoss = new() { Policy = EffectTargetLossPolicy.Skip } },
            Hit(1) with { Target = EffectTarget.SELF, TargetResource = "missing" });
        var hash = CanonicalJson.ComputeHash(request.Combat);
        var result = EffectTransactionTests.Executor().Execute(request);
        Assert.True(result.IsFailure);
        Assert.Contains("missing", result.Error);
        Assert.Equal(hash, CanonicalJson.ComputeHash(request.Combat));
    }

    private static EffectDefinition Hit(float amount) => EffectTransactionTests.Resource(EffectType.DAMAGE, amount);

    private static EffectTriggerExecutionRequest Request(EffectDefinition effect, bool secondEnemy) =>
        Request([effect], secondEnemy);

    private static EffectTriggerExecutionRequest Request(params EffectDefinition[] effects) => Request(effects, false);

    private static EffectTriggerExecutionRequest Request(EffectDefinition[] effects, bool secondEnemy)
    {
        var request = EffectTransactionTests.Request(effects);
        var enemy = request.Combat.GetActor("enemy")!;
        var definition = enemy.GetResource("focus")!.Definition! with
        {
            ThresholdPolicies = [new()
            {
                PolicyId = "defeat", Comparison = ResourceThresholdComparison.LessThanOrEqual,
                ThresholdSource = ResourceThresholdSource.Minimum,
                Consequence = ResourceThresholdConsequence.DefeatOwner
            }]
        };
        enemy = enemy.WithResourceState(new() { OwnerId = "enemy", Resources = new Dictionary<string, ResourcePool>
        { ["focus"] = ResourcePool.Materialize(definition, 10, 40) } });
        var actors = new List<CombatActorState> { request.Combat.GetActor("hero")!, enemy };
        if (secondEnemy) actors.Add(enemy with { InstanceId = "enemy_b", ResourceState = new()
        { OwnerId = "enemy_b", Resources = new Dictionary<string, ResourcePool>
            { ["focus"] = ResourcePool.Materialize(definition, 30, 40) } } });
        return request with { Combat = CombatTransitions.Create(actors, request.Combat.Determinism) };
    }
}
