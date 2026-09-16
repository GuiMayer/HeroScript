using Core.CardZones;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Xunit;

namespace Core.Tests.Effects;

public sealed class CardZoneRunEffectTests
{
    [Fact]
    public void GenericEffect_MovesCardsInTargetActorZoneWithoutInterpretingItsPurpose()
    {
        var cardId = Guid.Parse("10000000-0000-8000-8000-000000000001");
        var ready = new CardZoneAddress { ZoneId = "ready", OwnerId = "enemy" };
        var spent = new CardZoneAddress { ZoneId = "spent", OwnerId = "enemy" };
        var graph = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "actor-abilities",
            Zones =
            [
                new CardZoneDefinition { ZoneId = "ready", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered },
                new CardZoneDefinition { ZoneId = "spent", OwnerScope = CardZoneOwnerScope.Actor, Ordering = CardZoneOrdering.Ordered }
            ],
            Flows = [new CardZoneFlowDefinition
            {
                FlowId = "ability.spend", AllowedInvocations = [CardZoneFlowInvocation.Effect],
                Steps = [new CardZoneFlowStepDefinition
                {
                    StepId = "move", Operation = CardZoneOperation.Move,
                    SourceZoneId = "ready", TargetZoneId = "spent",
                    Selection = new CardZoneSelectionDefinition { Strategy = CardZoneSelectionStrategy.First, Count = 1 }
                }]
            }]
        };
        var run = new RunState
        {
            PlayerEntityId = "hero",
            ResolvedMode = new ResolvedGameMode { CardZoneSystem = graph },
            Determinism = DeterministicContext.Create(91, "pinned-revision"),
            Deck = new DeckState { Topology = new CardZoneTopologyState
            {
                Instances = new Dictionary<Guid, CardInstanceState>
                {
                    [cardId] = new() { CardInstanceId = cardId, DefinitionId = "ability", OwnerId = "enemy" }
                },
                Zones = new Dictionary<string, CardZoneState>
                {
                    [ready.Key] = new() { Address = ready, InstanceIds = [cardId] },
                    [spent.Key] = new() { Address = spent }
                }
            } }
        };
        var command = new ResolvedEffectCommand
        {
            EffectInstanceId = "ability.spend.1",
            SourceEntityId = "hero",
            TargetEntityIds = ["enemy"],
            Definition = new EffectDefinition
            {
                EffectId = "ability.spend", Type = EffectType.CARD_ZONE_FLOW,
                CardZoneFlowId = "ability.spend", Target = EffectTarget.TARGET
            }
        };
        var combat = GameplayOwnershipTests.State();
        var executor = new CardZoneFlowExecutor();

        var first = RunEffectReducer.Apply(run, combat, command, null, "pinned-revision", executor);
        var repeated = RunEffectReducer.Apply(run, combat, command, null, "pinned-revision", executor);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value.Run.Deck.Topology),
            CanonicalJson.ComputeHash(repeated.Value.Run.Deck.Topology));
        Assert.Empty(first.Value.Run.Deck.Topology.GetZone(ready)!.InstanceIds);
        Assert.Equal(cardId, Assert.Single(first.Value.Run.Deck.Topology.GetZone(spent)!.InstanceIds));
        Assert.Equal(cardId, Assert.Single(first.Value.Record.CardInstanceIds));
        Assert.Equal("ability.spend", Assert.Single(first.Value.Record.CardZoneSteps).FlowId);
        Assert.Equal(ready.Key, first.Value.Record.CardZoneSteps[0].SourceAddress);
        Assert.Equal(spent.Key, first.Value.Record.CardZoneSteps[0].TargetAddress);
        Assert.Equal(cardId, Assert.Single(run.Deck.Topology.GetZone(ready)!.InstanceIds));
    }
}
