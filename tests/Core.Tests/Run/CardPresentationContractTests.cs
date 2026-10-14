using System.Text.Json;
using Core.Determinism;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardPresentationContractTests
{
    [Fact]
    public void MechanicalFixturesDeclareAmountsResourcesAndAlternativesWithoutVisualText()
    {
        foreach (var (name, effect, parameter, value) in CardPresentationFixtures.Cases)
        {
            var card = CardPresentationFixtures.Card(name, effect);
            Assert.Empty(card.Metadata);
            var cost = Assert.IsType<CardCostComponentDefinition>(card.Components[0]);
            Assert.Equal(1, Assert.Single(cost.Costs.Costs).Amount);
            Assert.Equal(2, Assert.Single(Assert.Single(cost.Costs.AlternativeCosts).Costs).Amount);
            if (parameter == Core.Effects.EffectNumericParameter.Amount)
                Assert.Equal(value, effect.FlatValue);
            if (name == "contextual") Assert.NotNull(effect.FormulaValue);
            if (name == "condensation") Assert.NotNull(effect.CondensationRecipeId);
        }
    }
    [Fact]
    public void OfferedCardCannotPretendToBeAnOwnedInstance()
    {
        var offer = new CardPresentationIdentity { Kind = CardPresentationSubjectKind.Offer, DefinitionId = "strike", OfferId = "reward:1" };
        Assert.True(offer.Validate().IsSuccess);
        Assert.True((offer with { CardInstanceId = Guid.NewGuid() }).Validate().IsFailure);
        Assert.True((offer with { OfferId = null }).Validate().IsFailure);
        Assert.True(new CardPresentationIdentity { Kind = CardPresentationSubjectKind.Definition, DefinitionId = "strike" }.Validate().IsSuccess);
    }

    [Fact]
    public void ColorReferencesDistinguishPermanentAndContextualChangesWithoutTargetOutcomes()
    {
        var layers = new CardNumericLayers { Original = 8, ComposedBase = 10, Source = 15, UnitId = "points" };
        Assert.Equal(new CardValueComparison(10, 8, "Original"), layers.Compare(CardFaceValueMode.ComposedBase));
        Assert.Equal(new CardValueComparison(15, 10, "ComposedBase"), layers.Compare(CardFaceValueMode.Source));
        Assert.DoesNotContain("impact", JsonSerializer.Serialize(layers), StringComparison.OrdinalIgnoreCase);
        Assert.Null((layers with { Source = null }).Compare(CardFaceValueMode.Source).Value);
    }

    [Fact]
    public void UnknownValueIsExplicitAndSerializationKeepsStableIdentity()
    {
        var facts = new CardPresentationFacts { Identity = new() { DefinitionId = "dynamic" },
            Version = new() { ContentRevision = "revision" }, Effects = [new() { EffectPath = "main", Parameters = [new(
                Core.Effects.EffectNumericParameter.Amount, new() { Expression = "target.resources.mana.current / 2", UnresolvedReason = "context_required" })] }] };
        var restored = JsonSerializer.Deserialize<CardPresentationFacts>(JsonSerializer.Serialize(facts))!;
        Assert.Equal(CanonicalJson.ComputeHash(facts), CanonicalJson.ComputeHash(restored));
        Assert.Null(restored.Effects[0].Parameters[0].Values.ComposedBase);
        Assert.Null(restored.Identity.CardInstanceId);
    }
}
