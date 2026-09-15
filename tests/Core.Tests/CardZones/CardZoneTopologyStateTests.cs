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

    private static CardInstanceState Card(Guid id) => new()
    {
        CardInstanceId = id,
        DefinitionId = "spark",
        OwnerId = "mage"
    };
}
