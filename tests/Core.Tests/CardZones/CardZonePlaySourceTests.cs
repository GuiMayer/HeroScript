using Core.CardZones;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZonePlaySourceTests
{
    private static readonly Guid Prepared = Guid.Parse("10000000-0000-8000-8000-000000000001");
    private static readonly Guid Inventory = Guid.Parse("10000000-0000-8000-8000-000000000002");
    private static readonly Guid Opponent = Guid.Parse("10000000-0000-8000-8000-000000000003");

    [Fact]
    public void ActorZones_GrantCardPlayOnlyFromExplicitlyAllowedOwnedZones()
    {
        var prepared = new CardZoneAddress { ZoneId = "prepared", OwnerId = "player" };
        var inventory = new CardZoneAddress { ZoneId = "inventory", OwnerId = "player" };
        var opponent = new CardZoneAddress { ZoneId = "prepared", OwnerId = "enemy" };
        var run = new RunState
        {
            PlayerEntityId = "player",
            ResolvedMode = new ResolvedGameMode
            {
                CardZoneSystem = new CardZoneSystemDefinition
                {
                    CardZoneSystemId = "actor-cards",
                    Zones =
                    [
                        new CardZoneDefinition { ZoneId = "prepared", OwnerScope = CardZoneOwnerScope.Actor, AllowsCardPlay = true },
                        new CardZoneDefinition { ZoneId = "inventory", OwnerScope = CardZoneOwnerScope.Actor }
                    ]
                }
            },
            Deck = new DeckState
            {
                Topology = new CardZoneTopologyState
                {
                    Instances = new Dictionary<Guid, CardInstanceState>
                    {
                        [Prepared] = Card(Prepared, "player"),
                        [Inventory] = Card(Inventory, "player"),
                        [Opponent] = Card(Opponent, "enemy")
                    },
                    Zones = new Dictionary<string, CardZoneState>
                    {
                        [prepared.Key] = new() { Address = prepared, InstanceIds = [Prepared] },
                        [inventory.Key] = new() { Address = inventory, InstanceIds = [Inventory] },
                        [opponent.Key] = new() { Address = opponent, InstanceIds = [Opponent] }
                    }
                }
            }
        };

        Assert.Equal([Prepared], CardZonePlaySource.CardsForActor(run, "player"));
        Assert.Equal([Opponent], CardZonePlaySource.CardsForActor(run, "enemy"));
        Assert.False(CardZonePlaySource.Contains(run, "player", Inventory));
        Assert.False(CardZonePlaySource.Contains(run, "player", Opponent));
    }

    [Fact]
    public void MissingZoneGraph_GrantsNoImplicitPlaySource()
    {
        var run = new RunState { PlayerEntityId = "player" };

        Assert.Empty(CardZonePlaySource.CardsForActor(run, "player"));
        Assert.Empty(CardZonePlaySource.CardsForActor(run, "enemy"));
    }

    private static CardInstanceState Card(Guid id, string owner) => new()
    {
        CardInstanceId = id, DefinitionId = "ability", OwnerId = owner
    };
}
