using Core.Combat.Flow;
using Core.Determinism;
using Core.Effects;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyResidualTests
{
    [Fact]
    public async Task ExistingGuardReserve_PreservesItsOriginalReleaseValue()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var reserve = f.Play("w1_reserve");
        Assert.True(reserve.IsSuccess, reserve.IsFailure ? reserve.Error : null);
        var release = f.Play("w1_release_guard", reserve.Value.State);
        Assert.True(release.IsSuccess, release.IsFailure ? release.Error : null);
        Assert.Equal(13, TagSynergyExecutionFixture.Value(release.Value.State, "block"));
        Assert.Empty(release.Value.State.StatusEffects);
    }

    [Theory]
    [InlineData("w1_cinder_brand", 3, 4)]
    [InlineData("w1_volatile_rot", 6, 1)]
    public async Task OffensiveResidual_TicksExactlyForConfiguredOwnerBoundaries(string id, int potency, int ticks)
    {
        var f = await TagSynergyExecutionFixture.Create();
        var played = f.Play(id);
        Assert.True(played.IsSuccess, played.IsFailure ? played.Error : null);
        var current = played.Value.State;
        Assert.Equal(100, TagSynergyExecutionFixture.Value(current, "health", "enemy"));
        for (var i = 0; i < ticks + 1; i++) {
            var before = CanonicalJson.ComputeHash(current);
            var next = new CombatStatusLifecycle(f.Executor).Process(f.Run, current, StatusTriggerBoundary.EndActivation, "enemy");
            Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
            Assert.Equal(before, CanonicalJson.ComputeHash(current));
            current = next.Value.Combat;
            Assert.Equal(100 - potency * System.Math.Min(i + 1, ticks), TagSynergyExecutionFixture.Value(current, "health", "enemy"));
        }
        Assert.Empty(current.StatusEffects);
    }

    [Theory]
    [InlineData("w1_lingering_aegis", "block", 0, 3, 2)]
    [InlineData("w1_mending_echo", "health", 50, 2, 3)]
    [InlineData("w1_delayed_recovery", "health", 50, 8, 1)]
    public async Task SupportResidual_ProcsBeforeExpirationWithoutImmediateOrExtraTick(
        string id, string resource, int initial, int potency, int ticks)
    {
        var f = await TagSynergyExecutionFixture.Create();
        var played = f.Play(id);
        Assert.True(played.IsSuccess, played.IsFailure ? played.Error : null);
        var current = played.Value.State;
        Assert.Equal(initial, TagSynergyExecutionFixture.Value(current, resource));
        var unrelated = new CombatStatusLifecycle(f.Executor).Process(f.Run, current, StatusTriggerBoundary.StartActivation, "enemy").Value.Combat;
        Assert.Equal(initial, TagSynergyExecutionFixture.Value(unrelated, resource));
        for (var i = 0; i < ticks + 1; i++) {
            var next = new CombatStatusLifecycle(f.Executor).Process(f.Run, current, StatusTriggerBoundary.StartActivation, "hero");
            Assert.True(next.IsSuccess, next.IsFailure ? next.Error : null);
            current = next.Value.Combat;
            Assert.Equal(initial + potency * System.Math.Min(i + 1, ticks), TagSynergyExecutionFixture.Value(current, resource));
        }
        Assert.Empty(current.StatusEffects);
    }

    [Fact]
    public async Task HealingRelease_UsesPotencyNotFutureTicksAndDoesNotConsumeDefense()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var heal = f.Play("w1_mending_echo").Value.State;
        var defense = f.Play("w1_lingering_aegis", heal).Value.State;
        var released = f.Play("w1_release_mending", defense);
        Assert.True(released.IsSuccess, released.IsFailure ? released.Error : null);
        Assert.Equal(52, TagSynergyExecutionFixture.Value(released.Value.State, "health"));
        Assert.Equal("w1_lingering_aegis", Assert.Single(released.Value.State.StatusEffects["hero"]).StatusId);
    }

    [Fact]
    public async Task NewDamageStatus_CondensesWithoutNamedRecipeDependency()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var applied = f.Play("w1_volatile_rot").Value.State;
        var result = f.Play("w1_release", applied);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(91, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Empty(result.Value.State.StatusEffects);
    }

    [Fact]
    public async Task VitalSurge_UsesMaximumNotCurrentHealth()
    {
        var f = await TagSynergyExecutionFixture.Create(health: 10, critical: 300, armor: 1000);
        var result = f.Play("w1_vital_surge");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(20, TagSynergyExecutionFixture.Value(result.Value.State, "health"));
    }
}
