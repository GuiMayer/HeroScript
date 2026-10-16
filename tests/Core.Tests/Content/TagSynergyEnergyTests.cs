using Core.Determinism;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyEnergyTests
{
    [Theory]
    [InlineData(0, 2)] [InlineData(2, 3)] [InlineData(3, 3)]
    public async Task PowerSurge_ClampsWithoutChangingSource(float current, float expected)
    {
        var f = await TagSynergyExecutionFixture.Create(energy: current, critical: 250, armor: 5000, block: 20);
        var before = CanonicalJson.ComputeHash(f.Combat);
        var played = f.Play("w1_power_surge");
        Assert.True(played.IsSuccess, played.IsFailure ? played.Error : null);
        Assert.Equal(expected, TagSynergyExecutionFixture.Value(played.Value.State, "energy"));
        Assert.Equal(20, TagSynergyExecutionFixture.Value(played.Value.State, "block"));
        Assert.Equal(before, CanonicalJson.ComputeHash(f.Combat));
    }

    [Theory]
    [InlineData(4, false)] [InlineData(4.5f, false)] [InlineData(5, true)] [InlineData(50, true)]
    public async Task BloodFuel_UsesNonlethalUnmitigatedCost(float health, bool legal)
    {
        var f = await TagSynergyExecutionFixture.Create(health: health, energy: 0, block: 20, armor: 10000);
        var before = CanonicalJson.ComputeHash(f.Combat);
        var result = f.Play("w1_blood_fuel");
        Assert.Equal(legal, result.IsSuccess);
        if (legal) {
            Assert.Equal(health - 4, TagSynergyExecutionFixture.Value(result.Value.State, "health"));
            Assert.Equal(2, TagSynergyExecutionFixture.Value(result.Value.State, "energy"));
            Assert.Equal(20, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
        }
        Assert.Equal(before, CanonicalJson.ComputeHash(f.Combat));
    }

    [Fact]
    public async Task EnergyPayload_StoresThenConsumesExactlyOnce()
    {
        var f = await TagSynergyExecutionFixture.Create(energy: 1);
        var stored = f.Play("w1_charge_cell");
        Assert.True(stored.IsSuccess, stored.IsFailure ? stored.Error : null);
        Assert.Equal(0, TagSynergyExecutionFixture.Value(stored.Value.State, "energy"));
        var released = f.Play("w1_release_charge", stored.Value.State);
        Assert.True(released.IsSuccess, released.IsFailure ? released.Error : null);
        Assert.Equal(2, TagSynergyExecutionFixture.Value(released.Value.State, "energy"));
        Assert.Empty(released.Value.State.StatusEffects);
        var empty = f.Play("w1_release_charge", released.Value.State);
        Assert.True(empty.IsSuccess, empty.IsFailure ? empty.Error : null);
        Assert.Equal(2, TagSynergyExecutionFixture.Value(empty.Value.State, "energy"));
    }
}
