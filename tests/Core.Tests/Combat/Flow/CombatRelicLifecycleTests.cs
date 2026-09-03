using Core.Combat;
using Core.Combat.Flow;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Math;
using Core.Resources;
using Core.Run;
using Moq;
using Xunit;

namespace Core.Tests.Combat.Flow;

public sealed class CombatRelicLifecycleTests
{
    [Fact]
    public void CombatStart_ExecutesPinnedRelicTriggerThroughUniversalProcessor()
    {
        var relicId = Guid.Parse("50000000-0000-8000-8000-000000000001");
        var run = new RunState
        {
            Determinism = DeterministicContext.Create(1, "revision"),
            Relics =
            [
                new RunRelicState
                {
                    RelicInstanceId = relicId,
                    DefinitionId = "battery",
                    Triggers =
                    [
                        new EffectTriggerDefinition
                        {
                            TriggerId = "battery.start",
                            Boundary = CombatTriggerBoundaries.CombatStart,
                            Effects =
                            [
                                new EffectDefinition
                                {
                                    EffectId = "battery.energy",
                                    Type = EffectType.MODIFY_RESOURCE,
                                    Target = EffectTarget.SELF,
                                    FlatValue = 2,
                                    TargetResource = "energy",
                                    Operation = ResourceEffectOperation.ADD
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        var combat = CombatTransitions.Create(
            Entity("hero", true, 1),
            [Entity("enemy", false, 1)],
            DeterministicContext.Create(2, "revision"));
        var lifecycle = new CombatRelicLifecycle(new EffectTriggerExecutor(
            Mock.Of<IRuntimeFormulaEvaluator>(),
            new ImmutableEffectProcessor()));

        var result = lifecycle.Process(run, combat, CombatTriggerBoundaries.CombatStart);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(3, result.Value.Combat.Hero.GetResource("energy")!.Current);
        var application = Assert.Single(Assert.Single(result.Value.Events).Applications);
        Assert.Equal(EffectProvenanceKind.Relic, application.Provenance.Kind);
        Assert.Equal(relicId.ToString(), application.Provenance.SourceId);
    }

    private static CombatEntity Entity(string id, bool hero, float energy) => new()
    {
        EntityId = id,
        IsHero = hero,
        ResourceState = new EntityResourceState
        {
            EntityId = id,
            Resources = new Dictionary<string, ResourcePool>
            {
                ["energy"] = new()
                {
                    ResourceId = "energy",
                    Current = energy,
                    Maximum = 10,
                    Minimum = 0,
                    Definition = new ResourceDefinition
                    {
                        ResourceId = "energy",
                        DisplayName = "Energy",
                        DefaultMax = 10
                    }
                }
            }
        }
    };
}
