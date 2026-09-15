using Core.CardZones;
using Core.Determinism;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneBootstrapperTests
{
    [Fact]
    public void Create_UsesConfiguredZonesAndPlacementsWithDeterministicIdentities()
    {
        var system = Compile(
            Zone("library", CardZoneOwnerScope.RunOwner, CardZoneOrdering.Ordered),
            Zone("reserve", CardZoneOwnerScope.Actor, CardZoneOrdering.Unordered),
            Zone("shared", CardZoneOwnerScope.Global, CardZoneOrdering.Ordered));
        var plan = new CardZoneBootstrapPlan
        {
            RunOwnerId = "run-a",
            ActorIds = ["enemy", "hero"],
            Batches =
            [
                new CardZoneInitialBatch
                {
                    ZoneId = "library", OwnerId = "run-a", DefinitionIds = ["spark", "guard", "spark"]
                },
                new CardZoneInitialBatch
                {
                    ZoneId = "reserve", OwnerId = "hero", DefinitionIds = ["skill"]
                }
            ]
        };

        var first = CardZoneBootstrapper.Create(system, plan, DeterministicContext.Create(42, "content-a"));
        var repeated = CardZoneBootstrapper.Create(system, plan, DeterministicContext.Create(42, "content-a"));

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(first.Value.Fingerprint, repeated.Value.Fingerprint);
        Assert.True(first.Value.CreatedInstanceIds.SequenceEqual(repeated.Value.CreatedInstanceIds));
        Assert.Equal(4, first.Value.State.Instances.Count);
        Assert.Equal(4, first.Value.State.Zones.Count);
        Assert.Equal(3, first.Value.State.GetZone("library", "run-a")!.InstanceIds.Count);
        Assert.Single(first.Value.State.GetZone("reserve", "hero")!.InstanceIds);
        Assert.Empty(first.Value.State.GetZone("shared", "$global")!.InstanceIds);
    }

    [Fact]
    public void Create_FailsAtomicallyWhenPlacementExceedsConfiguredCapacity()
    {
        var system = Compile(Zone("slot", CardZoneOwnerScope.RunOwner,
            CardZoneOrdering.Ordered, capacity: 1));
        var plan = new CardZoneBootstrapPlan
        {
            RunOwnerId = "run-a",
            Batches = [new CardZoneInitialBatch
            {
                ZoneId = "slot", OwnerId = "run-a", DefinitionIds = ["one", "two"]
            }]
        };
        var original = DeterministicContext.Create(9, "content-a");

        var result = CardZoneBootstrapper.Create(system, plan, original);

        Assert.True(result.IsFailure);
        Assert.Contains("capacity", result.Error);
        Assert.Equal(0UL, original.IdSequence);
    }

    [Fact]
    public void Create_PreservesPreconfiguredCardUpgradesWithoutMutatingInput()
    {
        var system = Compile(Zone("slot", CardZoneOwnerScope.RunOwner, CardZoneOrdering.Ordered));
        var upgrades = new List<CardUpgradeState> { new() { UpgradeId = "boost-one" } };
        var plan = new CardZoneBootstrapPlan
        {
            RunOwnerId = "run-a",
            Batches = [new CardZoneInitialBatch
            {
                ZoneId = "slot", OwnerId = "run-a",
                Cards = [new CardZoneCardCreation { DefinitionId = "spark", Upgrades = upgrades }]
            }]
        };
        upgrades.Clear();

        var initialized = CardZoneBootstrapper.Create(system, plan,
            DeterministicContext.Create(9, "content-a"));

        Assert.True(initialized.IsSuccess, initialized.IsFailure ? initialized.Error : null);
        var instance = Assert.Single(initialized.Value.State.Instances.Values);
        Assert.Equal("boost-one", Assert.Single(instance.Upgrades).UpgradeId);
    }

    private static CompiledCardZoneSystem Compile(params CardZoneDefinition[] zones)
    {
        var result = CardZoneSystemCompiler.Compile(new CardZoneSystemDefinition
        {
            CardZoneSystemId = "configured-zones", Zones = zones
        });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        return result.Value;
    }

    private static CardZoneDefinition Zone(string id, CardZoneOwnerScope owner,
        CardZoneOrdering ordering, int? capacity = null) => new()
    {
        ZoneId = id, OwnerScope = owner, Ordering = ordering, Capacity = capacity
    };
}
