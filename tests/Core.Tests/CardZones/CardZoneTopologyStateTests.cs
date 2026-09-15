using Core.CardZones;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneTopologyStateTests
{
    private static readonly Guid First = Guid.Parse("10000000-0000-8000-8000-000000000001");
    private static readonly Guid Second = Guid.Parse("10000000-0000-8000-8000-000000000002");

    [Fact]
    public void State_DefensivelyCopiesZonesAndValidatesSingleMembership()
    {
        var ids = new List<Guid> { First, Second };
        var address = new CardZoneAddress { ZoneId = "library", OwnerId = "mage" };
        var state = new CardZoneTopologyState
        {
            Instances = new Dictionary<Guid, CardInstanceState>
            {
                [First] = Card(First),
                [Second] = Card(Second)
            },
            Zones = new Dictionary<string, CardZoneState>
            {
                [address.Key] = new() { Address = address, InstanceIds = ids }
            }
        };

        ids.Clear();

        Assert.True(CardZoneTopologyValidator.Validate(state).IsSuccess);
        Assert.Equal([First, Second], state.GetZone(address)!.InstanceIds);
        Assert.Equal("library", state.FindZone(Second)!.Address.ZoneId);
    }

    [Fact]
    public void Validator_RejectsDuplicateMembershipWithoutMutatingState()
    {
        var firstAddress = new CardZoneAddress { ZoneId = "ready", OwnerId = "mage" };
        var secondAddress = new CardZoneAddress { ZoneId = "spent", OwnerId = "mage" };
        var state = new CardZoneTopologyState
        {
            Instances = new Dictionary<Guid, CardInstanceState> { [First] = Card(First) },
            Zones = new Dictionary<string, CardZoneState>
            {
                [firstAddress.Key] = new() { Address = firstAddress, InstanceIds = [First] },
                [secondAddress.Key] = new() { Address = secondAddress, InstanceIds = [First] }
            }
        };

        var result = CardZoneTopologyValidator.Validate(state);

        Assert.True(result.IsFailure);
        Assert.Contains("more than one zone", result.Error);
        Assert.Single(state.GetZone(firstAddress)!.InstanceIds);
    }

    [Fact]
    public void Validator_RejectsSnapshotThatContradictsCompiledCapacityAndOrdering()
    {
        var system = Compile(new CardZoneDefinition
        {
            ZoneId = "library", OwnerScope = CardZoneOwnerScope.RunOwner,
            Ordering = CardZoneOrdering.Unordered, Capacity = 1
        });
        var address = new CardZoneAddress { ZoneId = "library", OwnerId = "run-a" };
        var state = new CardZoneTopologyState
        {
            Instances = new Dictionary<Guid, CardInstanceState>
            {
                [First] = Card(First), [Second] = Card(Second)
            },
            Zones = new Dictionary<string, CardZoneState>
            {
                [address.Key] = new() { Address = address, InstanceIds = [Second, First] }
            }
        };

        Assert.True(CardZoneTopologyValidator.Validate(state).IsSuccess);
        Assert.Contains("capacity", CardZoneTopologyValidator.ValidateAgainstSystem(state, system, "run-a").Error);
        var oneCard = state with
        {
            Instances = new Dictionary<Guid, CardInstanceState> { [First] = Card(First) },
            Zones = new Dictionary<string, CardZoneState>
            {
                [address.Key] = new() { Address = address, InstanceIds = [First] }
            }
        };
        Assert.True(CardZoneTopologyValidator.ValidateAgainstSystem(oneCard, system, "run-a").IsSuccess);
        Assert.Contains("wrong owner", CardZoneTopologyValidator.ValidateAgainstSystem(oneCard, system, "run-b").Error);
    }

    [Fact]
    public void Validator_RejectsNonCanonicalUnorderedZone()
    {
        var system = Compile(new CardZoneDefinition
        {
            ZoneId = "library", OwnerScope = CardZoneOwnerScope.RunOwner,
            Ordering = CardZoneOrdering.Unordered
        });
        var address = new CardZoneAddress { ZoneId = "library", OwnerId = "run-a" };
        var state = new CardZoneTopologyState
        {
            Instances = new Dictionary<Guid, CardInstanceState>
            {
                [First] = Card(First), [Second] = Card(Second)
            },
            Zones = new Dictionary<string, CardZoneState>
            {
                [address.Key] = new() { Address = address, InstanceIds = [Second, First] }
            }
        };

        Assert.Contains("canonical identity order",
            CardZoneTopologyValidator.ValidateAgainstSystem(state, system, "run-a").Error);
    }

    private static CompiledCardZoneSystem Compile(CardZoneDefinition zone)
    {
        var compiled = CardZoneSystemCompiler.Compile(new CardZoneSystemDefinition
        {
            CardZoneSystemId = "test-zones",
            Zones = [zone]
        });
        Assert.True(compiled.IsSuccess, compiled.IsFailure ? compiled.Error : null);
        return compiled.Value;
    }

    private static CardInstanceState Card(Guid id) => new()
    {
        CardInstanceId = id,
        DefinitionId = "spark",
        OwnerId = "mage"
    };
}
