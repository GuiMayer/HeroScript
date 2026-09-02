using System.Text.Json;
using Core.Determinism;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunCollectibleTransitionsTests
{
    [Fact]
    public void CardInstances_AreUniqueReproducibleAndFollowTheirZone()
    {
        var first = DeckTransitions.Create(
            ["strike", "strike", "defend"],
            DeterministicContext.Create(42, "content-v1")).Value;
        var second = DeckTransitions.Create(
            ["strike", "strike", "defend"],
            DeterministicContext.Create(42, "content-v1")).Value;

        Assert.Equal(first.State.DrawPileInstanceIds, second.State.DrawPileInstanceIds);
        Assert.Equal(3, first.State.DrawPileInstanceIds.Distinct().Count());
        Assert.All(first.State.DrawPileInstanceIds, instanceId =>
            Assert.True(first.State.CardInstances.ContainsKey(instanceId)));

        var drawn = DeckTransitions.Draw(first.State, 1, first.Context).Value;
        Assert.Equal("strike", drawn.State.Hand[0]);
        Assert.Equal(first.State.DrawPileInstanceIds[0], drawn.State.HandInstanceIds[0]);
        Assert.Equal(first.State.DrawPileInstanceIds[1], drawn.State.DrawPileInstanceIds[0]);
        Assert.Empty(first.State.Hand);
    }

    [Fact]
    public void CardUpgrade_ReplacesOnlyTheSelectedInstance()
    {
        var created = DeckTransitions.Create(
            ["strike", "strike"],
            DeterministicContext.Create(7, "content-v1")).Value;
        var selectedId = created.State.DrawPileInstanceIds[1];
        var definition = new CardUpgradeDefinition
        {
            UpgradeId = "sharpened",
            CardDefinitionIds = ["strike"],
            Patches =
            [
                new CardEffectNumericPatchDefinition
                {
                    ComponentId = "effect.damage",
                    Attribute = CardEffectNumericAttribute.FlatValue,
                    Operation = CardNumericPatchOperation.Add,
                    Value = 3
                }
            ]
        };

        var upgraded = DeckTransitions.ApplyUpgrade(
            created.State,
            selectedId,
            definition,
            created.Context).Value.State;

        Assert.Empty(created.State.CardInstances[selectedId].Upgrades);
        Assert.Single(upgraded.CardInstances[selectedId].Upgrades);
        Assert.Empty(upgraded.CardInstances[created.State.DrawPileInstanceIds[0]].Upgrades);
        Assert.True(DeckTransitions.ApplyUpgrade(
            upgraded,
            selectedId,
            definition,
            created.Context).IsFailure);
    }

    [Fact]
    public void RelicAcquisition_IsDeterministicAndDoesNotMutateInput()
    {
        var definition = new RelicDefinition
        {
            RelicId = "ember_core",
            StackLimit = 2,
            Properties = new Dictionary<string, JsonElement>
            {
                ["amount"] = JsonSerializer.SerializeToElement(1)
            }
        };
        var initial = new RunState
        {
            RunId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Determinism = DeterministicContext.Create(99, "content-v1")
        };

        var first = RelicTransitions.Acquire(initial, definition).Value;
        var repeated = RelicTransitions.Acquire(initial, definition).Value;
        var stacked = RelicTransitions.Acquire(first.State, definition).Value;

        Assert.Empty(initial.Relics);
        Assert.Equal(first.Relic.RelicInstanceId, repeated.Relic.RelicInstanceId);
        Assert.Equal(2, stacked.Relic.Stacks);
        Assert.Equal(first.Relic.RelicInstanceId, stacked.Relic.RelicInstanceId);
        Assert.True(RelicsEqual(first.State, repeated.State));
    }

    private static bool RelicsEqual(RunState left, RunState right) =>
        left.Relics.SequenceEqual(right.Relics) && left.Determinism == right.Determinism;
}
