using System.Text.Json;
using Core.Content;
using Core.Determinism;
using Core.Run;
using Core.Run.Loadouts;
using Xunit;

namespace Core.Tests.Run;

public sealed class InscriptionContentTests
{
    [Fact]
    public void ContentIsPublishedThroughCanonicalRegistryAndGraph()
    {
        var result = new ContentGraphValidator().Validate(LoadoutTestContent.Bundle());
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.True(ContentKindRegistry.Default.Get("inscriptions").IsSuccess);
        Assert.True(ContentKindRegistry.Default.Get("loadout-policies").IsSuccess);
    }

    [Theory]
    [InlineData(InscriptionInventoryScope.Actor)]
    [InlineData(InscriptionInventoryScope.Run)]
    public void ScopeAndEquipmentWindowsAreExplicitAndImmutable(InscriptionInventoryScope scope)
    {
        var policy = LoadoutTestContent.Policy(scope);
        Assert.True(policy.Validate().IsSuccess);
        Assert.True((policy with { InventoryScope = null }).Validate().IsFailure);
        Assert.True((policy with { AllowDuplicateDefinitions = null }).Validate().IsFailure);
        Assert.True((policy with { EquipmentWindows = [] }).Validate().IsFailure);
        Assert.True((policy with { Slots = [new() { SlotId = "same" }, new() { SlotId = "same" }] }).Validate().IsFailure);
        Assert.True((policy with { Capacity = float.NaN }).Validate().IsFailure);
        Assert.True((policy with { EquipmentWindows = [(LoadoutEquipmentWindow)999] }).Validate().IsFailure);
        Assert.Equal(CanonicalJson.ComputeHash(policy), CanonicalJson.ComputeHash(JsonSerializer.Deserialize<LoadoutPolicyDefinition>(JsonSerializer.Serialize(policy))!));
    }

    [Fact]
    public void BonusesRequireExistingCharacterAttributeChannelBucketAndFormula()
    {
        var item = LoadoutTestContent.Inscription();
        var bonus = item.AttributeBonuses[0];
        foreach (var invalid in new[] {
            item with { EquipmentCost = -1 }, item with { AttributeBonuses = [] },
            item with { AttributeBonuses = [bonus with { AttributeId = "missing" }] },
            item with { AttributeBonuses = [bonus with { Influence = bonus.Influence with { Bucket = "missing" } }] },
            item with { AttributeBonuses = [bonus with { Influence = bonus.Influence with { Value = null, Formula = "missing_formula" } }] }
        }) Assert.False(new ContentGraphValidator().Validate(LoadoutTestContent.Bundle(invalid)).IsValid);
    }

    [Fact]
    public void OptOutModesDoNotAcquireASerializedPolicyOrChangeHashes()
    {
        var mode = new GameModeDefinition { ModeId = "without" };
        Assert.DoesNotContain("LoadoutPolicyId", JsonSerializer.Serialize(mode));
        Assert.DoesNotContain("loadoutPolicyId", CanonicalJson.Serialize(mode));
    }
}
