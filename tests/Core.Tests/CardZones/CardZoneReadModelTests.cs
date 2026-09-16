using Core.CardZones;
using Core.Run;
using Xunit;

namespace Core.Tests.CardZones;

public sealed class CardZoneReadModelTests
{
    [Fact]
    public void ActorAbilityZone_ProjectsAuthoredPlayPermissionIndependentOfPresentationSlot()
    {
        var id = Guid.Parse("10000000-0000-8000-8000-000000000011");
        var address = new CardZoneAddress { ZoneId = "prepared", OwnerId = "hero" };
        var run = new RunState
        {
            PlayerEntityId = "hero",
            ResolvedMode = new ResolvedGameMode
            {
                CardZoneSystem = new CardZoneSystemDefinition
                {
                    CardZoneSystemId = "abilities",
                    Zones = [new CardZoneDefinition
                    {
                        ZoneId = "prepared", OwnerScope = CardZoneOwnerScope.Actor,
                        AllowsCardPlay = true,
                        Presentation = new Dictionary<string, System.Text.Json.JsonElement>
                        {
                            ["slot"] = System.Text.Json.JsonSerializer.SerializeToElement("abilities")
                        }
                    }]
                }
            },
            Deck = new DeckState { Topology = new CardZoneTopologyState
            {
                Instances = new Dictionary<Guid, CardInstanceState>
                {
                    [id] = new() { CardInstanceId = id, DefinitionId = "skill", OwnerId = "hero" }
                },
                Zones = new Dictionary<string, CardZoneState>
                {
                    [address.Key] = new() { Address = address, InstanceIds = [id] }
                }
            } }
        };

        var view = Assert.Single(CardZoneReadModel.Project(run).Zones);
        Assert.True(view.AllowsCardPlay);
        Assert.Equal("abilities", view.Presentation["slot"].GetString());
        Assert.Equal(id, Assert.Single(view.Cards).CardInstanceId);
    }

    [Fact]
    public void ToolOnlyVisibility_RevealsContentsAndOrderOnlyWhenModeGrantsZoneTools()
    {
        var first = Guid.Parse("10000000-0000-8000-8000-000000000001");
        var second = Guid.Parse("10000000-0000-8000-8000-000000000002");
        var address = new CardZoneAddress { ZoneId = "mystery", OwnerId = "$run" };
        var run = new RunState
        {
            PlayerEntityId = "hero",
            ResolvedMode = new ResolvedGameMode
            {
                CardZoneSystem = new CardZoneSystemDefinition
                {
                    CardZoneSystemId = "hidden",
                    Zones = [new CardZoneDefinition
                    {
                        ZoneId = "mystery", OwnerScope = CardZoneOwnerScope.RunOwner,
                        Visibility = new CardZoneVisibilityDefinition
                        {
                            Contents = CardZoneVisibility.ToolCapability,
                            Order = CardZoneOrderVisibility.ToolCapability
                        }
                    }]
                }
            },
            Deck = new DeckState { Topology = new CardZoneTopologyState
            {
                Instances = new Dictionary<Guid, CardInstanceState>
                {
                    [first] = new() { CardInstanceId = first, DefinitionId = "a" },
                    [second] = new() { CardInstanceId = second, DefinitionId = "b" }
                },
                Zones = new Dictionary<string, CardZoneState>
                {
                    [address.Key] = new() { Address = address, InstanceIds = [second, first] }
                }
            } }
        };

        var normal = Assert.Single(CardZoneReadModel.Project(run).Zones);
        var tools = Assert.Single(CardZoneReadModel.Project(run with
        {
            ResolvedMode = run.ResolvedMode! with
            {
                CapabilityPolicy = new CapabilityPolicyDefinition { AllowCardZoneCheats = true }
            }
        }).Zones);

        Assert.Equal(2, normal.Count);
        Assert.False(normal.ContentsVisible);
        Assert.Empty(normal.Cards);
        Assert.True(tools.ContentsVisible);
        Assert.True(tools.OrderVisible);
        Assert.Equal(second, tools.Cards[0].CardInstanceId);
        Assert.Equal(first, tools.Cards[1].CardInstanceId);
    }
}
