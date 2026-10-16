using Core.Run;
using Core.Run.Content;
using Core.Determinism;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyCompatibilityTests
{
    [Theory]
    [InlineData("w1_calibration")] [InlineData("w1_amplify")] [InlineData("w1_no_critical_transform")]
    public async Task NonStructuralModifiers_SupportNewDamageCardsAndPinnedTagPredicates(string id)
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", id).Value;
        Assert.Contains("w1_guarded_strike", upgrade.CardDefinitionIds);
        // The grammar can express tag predicates; published compatibility remains
        // curated because package validation also checks structural fixture cards.
        upgrade = upgrade with { CardDefinitionIds = [], Requirements = new() { RequiredTags = ["world_one", "attack"] } };
        var compiler = new CardContentCompiler();
        var card = compiler.Compile("w1_guarded_strike", runtime).Value with { CardId = "novel_attack" };
        var instance = new CardInstanceState { DefinitionId = "novel_attack", CardInstanceId = Guid.Parse("54000000-0000-4000-8000-000000000001") };
        var applied = CardInstanceUpgradeTransitions.Apply(instance, upgrade, runtime.Manifest.Revision);
        Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error : null);
        Assert.True(new EffectiveCardResolver().Resolve(card, applied.Value).IsSuccess);
        var withoutAttack = card with { Tags = ["world_one", "skill"] };
        Assert.True(new EffectiveCardResolver().Resolve(withoutAttack, applied.Value).IsFailure);
    }

    [Fact]
    public async Task AffinityAndWard_AddComponentsWithoutMutatingTheBase()
    {
        var runtime = await AscendantWorldOneContentTests.Compile();
        var card = new CardContentCompiler().Compile("w1_guarded_strike", runtime).Value;
        var before = CanonicalJson.ComputeHash(card);
        foreach (var id in new[] { "w1_fire_affinity", "w1_poison_affinity", "w1_ward_link" }) {
            var upgrade = runtime.GetDefinition<CardUpgradeDefinition>("card-upgrades", id).Value;
            var applied = CardInstanceUpgradeTransitions.Apply(new() { DefinitionId = card.CardId,
                CardInstanceId = Guid.Parse("54000000-0000-4000-8000-000000000002") }, upgrade, runtime.Manifest.Revision).Value;
            var effective = new EffectiveCardResolver().Resolve(card, applied);
            Assert.True(effective.IsSuccess, effective.IsFailure ? effective.Error : null);
            Assert.Contains(effective.Value.All<CardEffectComponentDefinition>(), component =>
                component.ComponentId == (id == "w1_ward_link" ? "upgrade.guard" : "upgrade.residual"));
        }
        Assert.Equal(before, CanonicalJson.ComputeHash(card));
    }
}
