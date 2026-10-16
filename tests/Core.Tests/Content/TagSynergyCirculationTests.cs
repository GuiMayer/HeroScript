using Core.CardZones;
using Core.Determinism;
using Core.Run;
using Core.Common;
using Core.Content;
using Core.Run.Content;
using Moq;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyCirculationTests
{
    [Fact]
    public async Task EffectiveTagAddedAndRemovedByTransformation_ChangesSelectionWithoutChangingConsumer()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var runtimes = new Mock<IContentRuntimeResolver>();
        runtimes.Setup(x => x.Resolve(f.Runtime.Manifest.Revision, It.IsAny<string?>()))
            .Returns(Result<ContentRuntime>.Success(f.Runtime));
        var resolver = new RevisionedCardZoneCardMetadataResolver(runtimes.Object, new CardContentCompiler(), new EffectiveCardResolver());
        var rules = new CardZoneRuntimeRuleEvaluator(f.Formulas, resolver);
        var context = new CardZoneFlowContext { ContentRevision = f.Runtime.Manifest.Revision, ConfigName = "ascendant" };
        var selector = new CardZoneSelectionDefinition { Strategy = CardZoneSelectionStrategy.ByTags, RequiredTags = ["defense"] };
        var card = new CardInstanceState { DefinitionId = "w1_strike",
            CardInstanceId = Guid.Parse("57000000-0000-4000-8000-000000000001") };
        Assert.False(rules.Matches(card, selector, context).Value);
        var upgraded = CardInstanceUpgradeTransitions.Apply(card, new() { UpgradeId = "novel_tag",
            Patches = [new CardTagsPatchDefinition { Add = ["defense"] }] }, f.Runtime.Manifest.Revision).Value;
        Assert.True(rules.Matches(upgraded, selector, context).Value);
        var removed = CardInstanceUpgradeTransitions.Remove(upgraded, 1, f.Runtime.Manifest.Revision).Value;
        Assert.False(rules.Matches(removed, selector, context).Value);
        Assert.Empty(card.Upgrades);
    }

    [Fact]
    public async Task FullHand_AllowsZeroMovesAndPreservesDrawOrderAndInstances()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var hand = new CardZoneAddress { ZoneId = "hand", OwnerId = "$run" };
        var draw = new CardZoneAddress { ZoneId = "draw", OwnerId = "$run" };
        var topology = CardZoneTransitions.CreateEmpty(new[] { "hand", "draw", "discard", "exhaust" }
            .Select(z => new CardZoneAddress { ZoneId = z, OwnerId = "$run" })).Value;
        var filled = CardZoneTransitions.CreateInstances(topology, hand, Enumerable.Repeat("w1_strike", 10).ToArray(),
            new(), new(), null, CardZoneOrdering.Ordered, f.Run.Determinism).Value;
        var prepared = CardZoneTransitions.CreateInstances(filled.State, draw, ["w1_guard", "w1_bulwark"],
            new(), new(), null, CardZoneOrdering.Ordered, filled.Context).Value;
        f = f with { Run = f.Run with { Deck = new() { Topology = prepared.State }, Determinism = prepared.Context } };
        var before = CanonicalJson.ComputeHash(f.Run.Deck);
        var result = f.Play("w1_gather_defense");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(before, CanonicalJson.ComputeHash(result.Value.Run!.Deck));
    }

    [Theory]
    [InlineData("w1_seek_attack", "draw", "hand", "w1_strike")]
    [InlineData("w1_gather_defense", "draw", "hand", "w1_guard")]
    [InlineData("w1_recover_trace", "discard", "hand", "w1_mending_echo")]
    [InlineData("w1_prepare_guard", "discard", "draw", "w1_guard")]
    public async Task Search_UsesTagsPreservesIdentityAndNeverSelectsResolvingCard(
        string id, string source, string target, string eligible)
    {
        var f = await TagSynergyExecutionFixture.Create();
        var addresses = new[] { "hand", "draw", "discard", "exhaust" }.Select(z =>
            new CardZoneAddress { ZoneId = z, OwnerId = "$run" }).ToArray();
        var topology = CardZoneTransitions.CreateEmpty(addresses).Value;
        var sourceAddress = addresses.Single(a => a.ZoneId == source);
        var created = CardZoneTransitions.CreateInstances(topology, sourceAddress, ["w1_focus", eligible, eligible],
            new(), new(), null, CardZoneOrdering.Ordered, f.Run.Determinism).Value;
        var inHand = CardZoneTransitions.CreateInstances(created.State, addresses[0], [id],
            new(), new(), null, CardZoneOrdering.Ordered, created.Context).Value;
        var fixture = f with { Run = f.Run with { Deck = new() { Topology = inHand.State }, Determinism = inHand.Context } };
        var before = CanonicalJson.ComputeHash(fixture.Run);
        var result = fixture.Play(id);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var next = result.Value.Run!.Deck.Topology;
        var moved = created.State.GetZone(sourceAddress)!.InstanceIds.Skip(1)
            .Take(id == "w1_gather_defense" ? 2 : 1).ToArray();
        Assert.All(moved, instance => Assert.Contains(instance, next.GetZone(addresses.Single(a => a.ZoneId == target))!.InstanceIds));
        Assert.Equal(inHand.State.Instances.Count, next.Instances.Count);
        Assert.Contains(inHand.CreatedInstanceIds.Single(), next.GetZone(addresses[0])!.InstanceIds);
        Assert.Equal(before, CanonicalJson.ComputeHash(fixture.Run));
        Assert.True(CardZoneTopologyValidator.Validate(next).IsSuccess);
    }

    [Fact]
    public async Task EmptySearch_IsExplicitPartialNotAnError()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var topology = CardZoneTransitions.CreateEmpty(new[] { "hand", "draw", "discard", "exhaust" }.Select(
            z => new CardZoneAddress { ZoneId = z, OwnerId = "$run" })).Value;
        f = f with { Run = f.Run with { Deck = new() { Topology = topology } } };
        var result = f.Play("w1_recover_trace");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Empty(result.Value.Run!.Deck.Topology.Instances);
    }
}
