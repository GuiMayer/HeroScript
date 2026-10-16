using System.Collections.Immutable;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyOffenseTests
{
    [Theory]
    [InlineData(0, 6)] [InlineData(100, 12)] [InlineData(200, 18)]
    public async Task NeedleStorm_HasFullPerHitBudgetAndIndependentTierRolls(float chance, float expected)
    {
        var f = await TagSynergyExecutionFixture.Create(critical: chance);
        var result = f.Play("w1_needle_storm");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(100 - expected, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(3, result.Value.Steps.Count(step => step.TargetEntityId == "enemy" && step.Applied));
        Assert.Equal(3, result.Value.Steps.SelectMany(step => step.RandomInputs).Select(input => input.ScopeId).Distinct().Count());
    }

    [Fact]
    public async Task HeavyImpact_UsesEntireEnergyBudget()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var result = f.Play("w1_heavy_impact");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(72, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(0, TagSynergyExecutionFixture.Value(result.Value.State, "energy"));
    }

    [Fact]
    public async Task BreakGuard_MutatesDefenseBeforeNormalDamageWithoutSelfMitigation()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var enemy = f.Combat.Actors["enemy"];
        var state = f.Combat with { Actors = f.Combat.Actors.ToImmutableDictionary().SetItem("enemy", enemy with {
            ResourceState = enemy.ResourceState with { Resources = enemy.ResourceState.Resources.ToImmutableDictionary().SetItem("block",
                TagSynergyExecutionFixture.Pool("block", 10, 999)) } }) };
        var before = CanonicalJson.ComputeHash(state);
        var result = f.Play("w1_break_guard", state);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, TagSynergyExecutionFixture.Value(result.Value.State, "block", "enemy"));
        Assert.Equal(98, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(before, CanonicalJson.ComputeHash(state));
    }

    [Fact]
    public async Task RelayStrike_TransfersOnlyAppliedDomainRemainder()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var enemy = f.Combat.Actors["enemy"];
        var state = f.Combat with { Actors = f.Combat.Actors.ToImmutableDictionary().SetItem("enemy", enemy with {
            ResourceState = enemy.ResourceState with { Resources = enemy.ResourceState.Resources.ToImmutableDictionary().SetItem("health",
                TagSynergyExecutionFixture.Pool("health", 10, 100)) } }) };
        var result = f.Play("w1_relay_strike", state);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(0, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(96, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy2"));
        Assert.Single(result.Value.Steps.Where(step => step.Continuation?.ToEntityId == "enemy2"));
    }
}
