using Core.CardZones;
using Core.Combat.Activation;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Run;
using Core.Run.Sandbox;
using Moq;
using Xunit;

namespace Core.Tests.Run;

public sealed class CombatSandboxSnapshotTests
{
    private static readonly Guid RunId = Guid.Parse("10000000-0000-8000-8000-000000000001");
    private static readonly Guid CombatId = Guid.Parse("20000000-0000-8000-8000-000000000001");
    private static readonly Guid PreparedCardId = Guid.Parse("30000000-0000-8000-8000-000000000001");
    private static readonly Guid InventoryCardId = Guid.Parse("30000000-0000-8000-8000-000000000002");

    [Fact]
    public void Get_ProjectsCardsFromAuthoredPlayableActorZones()
    {
        var prepared = new CardZoneAddress { ZoneId = "prepared", OwnerId = "hero" };
        var inventory = new CardZoneAddress { ZoneId = "inventory", OwnerId = "hero" };
        var run = new RunState
        {
            RunId = RunId,
            PlayerEntityId = "hero",
            Scenario = new CombatScenarioDefinition(),
            ActiveEncounterId = CombatId,
            Determinism = DeterministicContext.Create(7, "revision"),
            Encounters =
            [
                new RunEncounterState
                {
                    Combat = new CombatState
                    {
                        CombatId = CombatId,
                        ActivationState = new ActivationState { ActiveActorId = "hero" },
                        Determinism = DeterministicContext.Create(7, "revision")
                    }
                }
            ],
            ResolvedMode = new ResolvedGameMode
            {
                CardZoneSystem = new CardZoneSystemDefinition
                {
                    CardZoneSystemId = "prepared-abilities",
                    Zones =
                    [
                        new CardZoneDefinition
                        {
                            ZoneId = "prepared",
                            OwnerScope = CardZoneOwnerScope.Actor,
                            AllowsCardPlay = true
                        },
                        new CardZoneDefinition
                        {
                            ZoneId = "inventory",
                            OwnerScope = CardZoneOwnerScope.Actor
                        }
                    ]
                }
            },
            Deck = new DeckState
            {
                Topology = new CardZoneTopologyState
                {
                    Instances = new Dictionary<Guid, CardInstanceState>
                    {
                        [PreparedCardId] = Card(PreparedCardId, "prepared-card"),
                        [InventoryCardId] = Card(InventoryCardId, "inventory-card")
                    },
                    Zones = new Dictionary<string, CardZoneState>
                    {
                        [prepared.Key] = new() { Address = prepared, InstanceIds = [PreparedCardId] },
                        [inventory.Key] = new() { Address = inventory, InstanceIds = [InventoryCardId] }
                    }
                }
            }
        };
        var runs = new Mock<IRunQueryService>();
        runs.Setup(service => service.GetRun(RunId)).Returns(Result<RunState>.Success(run));

        var result = new CombatSandboxSnapshotService(runs.Object).Get(RunId);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        var card = Assert.Single(result.Value.PlayableCards);
        Assert.Equal(PreparedCardId, card.CardInstanceId);
        Assert.Equal("prepared-card", card.DefinitionId);
        Assert.Equal(0, card.PlayableIndex);
        Assert.Equal("prepared", card.ZoneId);
        Assert.Equal("hero", card.ZoneOwnerId);
    }

    private static CardInstanceState Card(Guid id, string definitionId) => new()
    {
        CardInstanceId = id,
        DefinitionId = definitionId,
        OwnerId = "hero"
    };
}
