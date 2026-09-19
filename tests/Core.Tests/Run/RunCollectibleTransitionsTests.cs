using System.Text.Json;
using Core.Determinism;
using Core.Run;
using Core.Calculations;
using Core.Effects;
using Core.Run.Content;
using Core.CardZones;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunCollectibleTransitionsTests
{
    [Fact]
    public void CardUpgrade_ReplacesOnlyTheSelectedInstance()
    {
        var zones = new CardZoneSystemDefinition
        {
            CardZoneSystemId = "upgrade-test",
            Zones = [new() { ZoneId = "collection", OwnerScope = CardZoneOwnerScope.RunOwner,
                Ordering = CardZoneOrdering.Ordered }]
        };
        var created = CardZoneBootstrapper.Create(CardZoneSystemCompiler.Compile(zones).Value,
            new CardZoneBootstrapPlan
            {
                RunOwnerId = "$run",
                Batches = [new() { ZoneId = "collection", OwnerId = "$run", DefinitionIds = ["strike", "strike"] }]
            }, DeterministicContext.Create(7, "content-v1")).Value;
        var instanceIds = created.State.GetZone("collection", "$run")!.InstanceIds;
        var selectedId = instanceIds[1];
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

        var upgraded = CardZoneTransitions.ApplyUpgrade(
            created.State,
            selectedId,
            definition,
            created.Context).Value.State;

        Assert.Empty(created.State.Instances[selectedId].Upgrades);
        Assert.Single(upgraded.Instances[selectedId].Upgrades);
        Assert.Empty(upgraded.Instances[instanceIds[0]].Upgrades);
        Assert.True(CardZoneTransitions.ApplyUpgrade(
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
            Influences =
            [
                new ContextualInfluenceDefinition
                {
                    InfluenceId = "ember.power",
                    Channel = "effect_amount",
                    Bucket = "flat",
                    Value = 1
                }
            ],
            Triggers =
            [
                new EffectTriggerDefinition
                {
                    TriggerId = "ember.start",
                    Boundary = "CombatStart",
                    Effects =
                    [
                        new EffectDefinition
                        {
                            EffectId = "ember.energy",
                            Type = EffectType.MODIFY_RESOURCE,
                            TargetResource = "energy"
                        }
                    ]
                }
            ],
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
        Assert.Equal("ember.power", Assert.Single(first.Relic.Influences).InfluenceId);
        Assert.Equal("ember.start", Assert.Single(first.Relic.Triggers).TriggerId);
        Assert.True(RelicsEqual(first.State, repeated.State));
    }

    private static bool RelicsEqual(RunState left, RunState right) =>
        left.Relics.SequenceEqual(right.Relics) && left.Determinism == right.Determinism;
}
