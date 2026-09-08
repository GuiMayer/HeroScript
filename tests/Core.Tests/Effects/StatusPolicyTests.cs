using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Effects;
using Core.StatusEffects;
using Core.Run;
using Xunit;

namespace Core.Tests.Effects;

public sealed class StatusPolicyTests
{
    [Theory]
    [InlineData(StackReapplyPolicy.Add, 5, 1)]
    [InlineData(StackReapplyPolicy.Replace, 3, 1)]
    [InlineData(StackReapplyPolicy.Highest, 3, 1)]
    [InlineData(StackReapplyPolicy.Independent, 2, 2)]
    public void ReapplyUsesConfiguredStacking(StackReapplyPolicy policy, int stacks, int instances)
    {
        var definition = new StatusEffectDefinition { StatusId = "test", DefaultDuration = 3, Stacking = policy };
        var first = Apply(GameplayOwnershipTests.State(), definition, 2, 3);
        var second = Apply(first, definition, 3, 5);
        Assert.Equal(instances, second.StatusEffects["enemy"].Length);
        Assert.Equal(stacks, second.StatusEffects["enemy"][0].Stacks);
        Assert.Equal(2, first.StatusEffects["enemy"][0].Stacks);
    }

    [Theory]
    [InlineData(DurationReapplyPolicy.Preserve, 2)]
    [InlineData(DurationReapplyPolicy.Refresh, 3)]
    [InlineData(DurationReapplyPolicy.Replace, 5)]
    [InlineData(DurationReapplyPolicy.Extend, 7)]
    [InlineData(DurationReapplyPolicy.Maximum, 5)]
    public void ReapplyUsesConfiguredDuration(DurationReapplyPolicy policy, int expected)
    {
        var definition = new StatusEffectDefinition { StatusId = "test", DefaultDuration = 3, DurationReapply = policy };
        var state = Apply(Apply(GameplayOwnershipTests.State(), definition, 1, 2), definition, 1, 5);
        Assert.Equal(expected, state.StatusEffects["enemy"][0].Duration);
    }

    [Fact]
    public void DispelFiltersTagsAndCountAndPreservesProtectedInstances()
    {
        var state = GameplayOwnershipTests.State();
        state = Apply(state, new() { StatusId = "protected", Dispellable = false, Tags = ["debuff"] }, 1, -1);
        state = Apply(state, new() { StatusId = "remove", Tags = ["debuff"] }, 1, -1);
        state = Apply(state, new() { StatusId = "buff", Tags = ["buff"] }, 1, -1);
        var result = new ImmutableEffectProcessor().Apply(state, [new()
        {
            EffectInstanceId = "dispel", TargetEntityIds = ["enemy"],
            Definition = new() { Type = EffectType.DISPEL_STATUS, Dispel = new() { RequiredTags = ["debuff"], MaximumInstances = 1 } }
        }]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(["protected", "buff"], result.Value.State.StatusEffects["enemy"].Select(item => item.StatusId));
        Assert.Single(result.Value.Records[0].RemovedStatusInstanceIds);
    }

    [Fact]
    public void RemovedStatusDoesNotTriggerLaterInBoundary()
    {
        var remove = new StatusEffectDefinition
        {
            StatusId = "first", Priority = 100,
            Triggers = [new() { TriggerId = "remove-second", Boundary = "EndActivation", Effects = [new()
                { Type = EffectType.REMOVE_STATUS, Target = EffectTarget.SELF, StatusId = "second" }] }]
        };
        var damage = new StatusEffectDefinition
        {
            StatusId = "second", Triggers = [new() { TriggerId = "damage", Boundary = "EndActivation", Effects = [new()
                { Type = EffectType.DAMAGE, Target = EffectTarget.SELF, TargetResource = "focus", FlatValue = 5 }] }]
        };
        var state = Apply(Apply(GameplayOwnershipTests.State(), remove, 1, -1), damage, 1, -1);
        var result = new CombatStatusLifecycle(EffectTransactionTests.Executor()).Process(new RunState(), state,
            StatusTriggerBoundary.EndActivation, "enemy");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(result.Value.Events);
        Assert.Equal(10, result.Value.Combat.GetActor("enemy")!.GetResource("focus")!.Current);
    }

    [Fact]
    public void DeclarativeConstraintDeniesOnlyMatchingTags()
    {
        var state = Apply(GameplayOwnershipTests.State(), new() { StatusId = "silence",
            ActionConstraints = [new() { ConstraintId = "no-spells", RequiredActionTags = ["spell"] }] }, 1, -1);
        Assert.Single(StatusActionConstraints.Evaluate(state, state.GetActor("enemy")!, new HashSet<string> { "spell" }, null, "revision").Value);
        Assert.Empty(StatusActionConstraints.Evaluate(state, state.GetActor("enemy")!, new HashSet<string> { "attack" }, null, "revision").Value);
    }

    private static CombatState Apply(CombatState state, StatusEffectDefinition definition, int stacks, int duration)
    {
        var result = new ImmutableEffectProcessor().Apply(state, [new()
        {
            EffectInstanceId = definition.StatusId, TargetEntityIds = ["enemy"], SourceEntityId = "hero",
            Definition = new() { Type = EffectType.APPLY_STATUS, StatusId = definition.StatusId, StatusStacks = stacks, StatusDuration = duration },
            StatusDefinition = definition
        }]);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value.State;
    }
}
