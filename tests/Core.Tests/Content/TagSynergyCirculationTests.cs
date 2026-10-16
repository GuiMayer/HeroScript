using Core.CardZones;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyCirculationTests
{
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
