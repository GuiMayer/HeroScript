using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardBasePresentationTests
{
    [Fact]
    public void EveryMechanicalFixtureHasTheSameFactsInAnOfferAndOwnedBase()
    {
        foreach (var (name, effect, parameter, value) in CardPresentationFixtures.Cases)
        {
            var compiled = new CardContentCompiler().Compile(CardPresentationFixtures.Card(name, effect));
            Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
            var definition = compiled.Value;
            var before = CanonicalJson.ComputeHash(definition);
            var owned = new EffectiveCardResolver().Resolve(definition, new() { CardInstanceId = Guid.NewGuid(), DefinitionId = name }).Value;
            var version = new CardPresentationVersion { ContentRevision = "revision" };
            var offer = CardBasePresentationProjector.Project(definition, CardBasePresentationProjector.DefinitionBase(definition),
                new() { Kind = CardPresentationSubjectKind.Offer, DefinitionId = name, OfferId = "selection:1" }, version).Value;
            var instance = CardBasePresentationProjector.Project(definition, owned,
                new() { Kind = CardPresentationSubjectKind.Instance, DefinitionId = name, CardInstanceId = owned.CardInstanceId }, version).Value;
            Assert.Equal(CanonicalJson.ComputeHash(offer.Costs), CanonicalJson.ComputeHash(instance.Costs));
            Assert.Equal(CanonicalJson.ComputeHash(offer.Effects), CanonicalJson.ComputeHash(instance.Effects));
            Assert.Null(offer.Identity.CardInstanceId);
            if (parameter != null)
                Assert.Equal(value, offer.Effects[0].Parameters.Single(item => item.Parameter == parameter).Values.ComposedBase);
            if (name == "contextual") Assert.Equal("expression_context_required", offer.Effects[0].Parameters[0].Values.UnresolvedReason);
            Assert.Equal(before, CanonicalJson.ComputeHash(definition));
        }
    }

    [Fact]
    public void AModifiedOfferShowsItsComposedBaseNotActorScaling()
    {
        var definition = new CardContentCompiler().Compile(CardPresentationFixtures.Card("strike", new() {
            Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = 8 })).Value;
        var instance = new CardInstanceState { DefinitionId = "strike", CardInstanceId = Guid.NewGuid(), Upgrades = [new() {
            UpgradeId = "added-base", TransformationId = 1, ContentRevision = "revision", Patches = [new CardEffectNumericPatchDefinition {
                ComponentId = "main", Attribute = CardEffectNumericAttribute.FlatValue, Value = 2 }] }] };
        var effective = new EffectiveCardResolver().Resolve(definition, instance);
        Assert.True(effective.IsSuccess, effective.IsFailure ? effective.Error : null);
        var result = CardBasePresentationProjector.Project(definition, effective.Value,
            new() { Kind = CardPresentationSubjectKind.Offer, DefinitionId = "strike", OfferId = "upgraded-offer" }, new() { ContentRevision = "revision" });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var values = result.Value.Effects[0].Parameters[0].Values;
        Assert.Equal(8, values.Original); Assert.Equal(10, values.ComposedBase); Assert.Null(values.Source);
        Assert.Null(result.Value.Identity.CardInstanceId);
        Assert.Single(result.Value.PermanentChanges);
    }

    [Fact]
    public void TriggerAndChildIdentityStaySeparateAndReadingDoesNotCreateGameplay()
    {
        var card = CardPresentationFixtures.Card("tree", new() { Type = EffectType.DAMAGE, TargetResource = "health", FlatValue = 8,
            ChainedEffects = [new() { Type = EffectType.HEAL, TargetResource = "mana", FlatValue = 3 }] });
        var definition = new CardContentCompiler().Compile(card).Value;
        var facts = CardBasePresentationProjector.Project(definition, CardBasePresentationProjector.DefinitionBase(definition),
            new() { DefinitionId = "tree" }, new() { ContentRevision = "revision" }).Value;
        Assert.Equal(2, facts.Effects.Length);
        Assert.Equal("main", facts.Effects[1].ParentEffectPath);
        Assert.Equal("main/children/0", facts.Effects[1].EffectPath);
        Assert.Null(facts.Version.RunId); Assert.Null(facts.Version.CombatId);
    }
}
