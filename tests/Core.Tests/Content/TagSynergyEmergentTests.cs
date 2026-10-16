using Core.Determinism;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyEmergentTests
{
    [Fact]
    public async Task CausticDefense_FeedsAnUnrelatedBlockAttackAndGenericCondensation()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var before = CanonicalJson.ComputeHash(f.Combat);
        var guarded = f.Play("w1_caustic_guard").Value.State;
        var attack = f.Play("w1_guarded_strike", guarded).Value.State;
        var released = f.Play("w1_release", attack).Value.State;
        Assert.Equal(91, TagSynergyExecutionFixture.Value(released, "health", "enemy"));
        Assert.Equal(3, TagSynergyExecutionFixture.Value(released, "block"));
        Assert.Equal(before, CanonicalJson.ComputeHash(f.Combat));
    }

    [Fact]
    public async Task DefensiveResidual_FeedsHealingAndAnAttackWithoutAConsumerNamingIt()
    {
        var f = await TagSynergyExecutionFixture.Create(block: 20);
        var healed = f.Play("w1_shelter").Value.State;
        var attack = f.Play("w1_guarded_strike", healed).Value.State;
        Assert.Equal(54, TagSynergyExecutionFixture.Value(attack, "health"));
        Assert.Equal(86, TagSynergyExecutionFixture.Value(attack, "health", "enemy"));
        Assert.Equal(20, TagSynergyExecutionFixture.Value(attack, "block"));
    }

    [Fact]
    public async Task EnergyReserve_UnlocksAThreeEnergyAttackAfterPreparation()
    {
        var f = await TagSynergyExecutionFixture.Create(energy: 2);
        var stored = f.Play("w1_charge_cell").Value.State;
        Assert.True(f.Play("w1_heavy_impact", stored).IsFailure);
        var restored = f.Play("w1_release_charge", stored).Value.State;
        var attack = f.Play("w1_heavy_impact", restored);
        Assert.True(attack.IsSuccess, attack.IsFailure ? attack.Error : null);
        Assert.Equal(72, TagSynergyExecutionFixture.Value(attack.Value.State, "health", "enemy"));
        Assert.Equal(0, TagSynergyExecutionFixture.Value(attack.Value.State, "energy"));
    }
}
