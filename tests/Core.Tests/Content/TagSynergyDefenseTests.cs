using Core.Determinism;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyDefenseTests
{
    [Theory]
    [InlineData(0, 7)] [InlineData(100, 14)] [InlineData(200, 21)]
    public async Task BlockDerivedDamage_StillUsesGlobalCriticalAndArmor(float critical, float expected)
    {
        var f = await TagSynergyExecutionFixture.Create(block: 20, critical: critical, armor: 100);
        var result = f.Play("w1_guarded_strike");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(100 - expected, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(20, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
    }

    [Theory]
    [InlineData(0, 4)] [InlineData(8, 8)] [InlineData(20, 14)]
    public async Task GuardedStrike_ReadsBlockWithoutSpendingIt(float block, float damage)
    {
        var f = await TagSynergyExecutionFixture.Create(block: block);
        var result = f.Play("w1_guarded_strike");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(100 - damage, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(block, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
    }

    [Fact]
    public async Task GuardWave_GivesEachEnemyFullValueThenConsumesOnce()
    {
        var f = await TagSynergyExecutionFixture.Create(block: 20);
        var before = CanonicalJson.ComputeHash(f.Combat);
        var result = f.Play("w1_guard_wave");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(90, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy"));
        Assert.Equal(90, TagSynergyExecutionFixture.Value(result.Value.State, "health", "enemy2"));
        Assert.Equal(0, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
        Assert.Equal(before, CanonicalJson.ComputeHash(f.Combat));
    }

    [Theory]
    [InlineData(100, 4)] [InlineData(50, 14)] [InlineData(10, 22)]
    public async Task DesperateGuard_UsesOnlyMissingHealth(float health, float expected)
    {
        var f = await TagSynergyExecutionFixture.Create(health: health);
        var result = f.Play("w1_desperate_guard");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(expected, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
    }

    [Fact]
    public async Task Shelter_UsesBlockButNeverCriticalOrArmor()
    {
        var f = await TagSynergyExecutionFixture.Create(block: 20, critical: 300, armor: 10000);
        var result = f.Play("w1_shelter");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(54, TagSynergyExecutionFixture.Value(result.Value.State, "health"));
        Assert.Equal(20, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
    }
}
