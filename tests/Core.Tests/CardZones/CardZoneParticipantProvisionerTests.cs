using Core.CardZones;
using Core.Determinism;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneParticipantProvisionerTests
{
    internal static CompiledCardZoneSystem System(bool actors = true) => CardZoneSystemCompiler.Compile(new()
    {
        CardZoneSystemId = "shared-pool",
        Zones = actors ? [Pool(), Pocket(), new() { ZoneId = "public", OwnerScope = CardZoneOwnerScope.Global, Ordering = CardZoneOrdering.Ordered }]
            : [Pool(), new() { ZoneId = "public", OwnerScope = CardZoneOwnerScope.Global, Ordering = CardZoneOrdering.Ordered }]
    }).Value;
    private static CardZoneDefinition Pool() => new() { ZoneId = "pool", OwnerScope = CardZoneOwnerScope.RunOwner, Ordering = CardZoneOrdering.Ordered };
    private static CardZoneDefinition Pocket() => new() { ZoneId = "pocket", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered, AllowsCardPlay = true };
    internal static CardZoneBootstrapResult Seed(CompiledCardZoneSystem system) => CardZoneBootstrapper.Create(system,
        new() { RunOwnerId = "$run", ActorIds = ["hero"], Batches = [new() { ZoneId = "pool", OwnerId = "$run", DefinitionIds = ["one", "two", "three"] }] },
        DeterministicContext.Create(7, "pinned")).Value;

    [Fact]
    public void Ensure_PreservesExistingInstancesAndDoesNotDealOrConsumeRandomness()
    {
        var system = System(); var seed = Seed(system);
        var original = CanonicalJson.ComputeHash(seed.State); var context = CanonicalJson.ComputeHash(seed.Context);
        var result = CardZoneParticipantProvisioner.Ensure(system, seed.State, ["hero", "dealer", "ally"], "$run").Value;
        Assert.Equal(2, result.CreatedAddresses.Length);
        Assert.Empty(result.State.GetZone("pocket", "dealer")!.InstanceIds);
        Assert.Same(seed.State.GetZone("pool", "$run"), result.State.GetZone("pool", "$run"));
        Assert.Same(seed.State.GetCard(seed.CreatedInstanceIds[0]), result.State.GetCard(seed.CreatedInstanceIds[0]));
        Assert.Equal(3, result.State.Instances.Count);
        Assert.Equal(original, CanonicalJson.ComputeHash(seed.State));
        Assert.Equal(context, CanonicalJson.ComputeHash(seed.Context));
    }

    [Fact]
    public void Ensure_IsIdempotentCanonicalAndAcceptsRepeatedParticipantReferences()
    {
        var system = System(); var seed = Seed(system);
        var first = CardZoneParticipantProvisioner.Ensure(system, seed.State, ["dealer", "hero", "dealer", "ally"], "$run").Value;
        var shuffled = CardZoneParticipantProvisioner.Ensure(system, seed.State, ["ally", "hero", "dealer"], "$run").Value;
        var again = CardZoneParticipantProvisioner.Ensure(system, first.State, ["ally", "hero", "dealer"], "$run").Value;
        Assert.Equal(CanonicalJson.ComputeHash(first.State), CanonicalJson.ComputeHash(shuffled.State));
        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.Empty(again.CreatedAddresses); Assert.Same(first.State, again.State);
        Assert.Equal(new[] { "ally", "dealer" }, first.CreatedAddresses.Select(address => address.OwnerId));
    }

    [Fact]
    public void Ensure_LaterParticipantsOnlyAddTheirMissingContainers()
    {
        var system = System(); var seed = Seed(system);
        var first = CardZoneParticipantProvisioner.Ensure(system, seed.State, ["hero", "dealer"], "$run").Value;
        var moved = CardZoneTransitions.Move(first.State, new() { ZoneId = "pool", OwnerId = "$run" },
            new() { ZoneId = "pocket", OwnerId = "dealer" }, [seed.CreatedInstanceIds[0]], new(), null, CardZoneOrdering.Ordered, CardZoneOrdering.Ordered, seed.Context);
        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error : null);
        var second = CardZoneParticipantProvisioner.Ensure(system, moved.Value.State, ["hero", "dealer", "later"], "$run").Value;
        Assert.Single(second.CreatedAddresses);
        Assert.Equal("later", second.CreatedAddresses[0].OwnerId);
        Assert.Equal(seed.CreatedInstanceIds[0], Assert.Single(second.State.GetZone("pocket", "dealer")!.InstanceIds));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("$global")]
    public void Ensure_RejectsInvalidParticipantsAtomically(string actor)
    {
        var system = System(); var seed = Seed(system); var before = CanonicalJson.ComputeHash(seed.State);
        Assert.True(CardZoneParticipantProvisioner.Ensure(system, seed.State, ["new", actor], "$run").IsFailure);
        Assert.Equal(before, CanonicalJson.ComputeHash(seed.State));
    }

    [Fact]
    public void Ensure_WithoutActorDefinitionsIsAnExactNoOp()
    {
        var system = System(false); var seed = Seed(system);
        var result = CardZoneParticipantProvisioner.Ensure(system, seed.State, ["hero", "dealer"], "$run").Value;
        Assert.Empty(result.CreatedAddresses); Assert.Same(seed.State, result.State);
    }
}
