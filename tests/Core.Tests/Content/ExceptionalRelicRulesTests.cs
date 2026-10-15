using System.Collections.Immutable;
using Core.Combat.Models;
using Core.Determinism;
using Core.Effects;
using Core.Resources;
using Core.Run;
using Core.Run.Content;
using Xunit;

namespace Core.Tests.Content;

public sealed class ExceptionalRelicRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(350)]
    public async Task BloodCovenantCapsBonusAfterOtherBonusesButKeepsTierAndActualLossRecovery(float chance)
    {
        var fixture = await HeroAttributeScalingTests.Fixture(0, chance);
        var request = Health(fixture.Request);
        request = Equip(request, fixture.Runtime.GetDefinition<RelicDefinition>("relics", "w1_blood_covenant").Value);
        request = request with { Card = new() { Components = [new CardInfluenceComponentDefinition
            { ComponentId = "large-bonus", Channel = "critical_bonus", Bucket = "bonus", Value = 10 }] } };
        var before = CanonicalJson.ComputeHash(request);
        var results = Enumerable.Range(0, 3).Select(_ => fixture.Executor.Execute(request)).ToArray();
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null));
        Assert.Single(results.Select(result => result.Value.Fingerprint).Distinct());
        var batch = results[0].Value;
        Assert.Equal(100, batch.Steps[0].Calculation!.Value);
        Assert.Equal(0, batch.Steps[0].RandomInputs[0].Captures["bonus"].Value);
        Assert.Equal(MathF.Floor(chance / 100), batch.Steps[0].RandomInputs[0].Calculation!.Checkpoints["guaranteed"]);
        Assert.Equal(40, batch.State.GetActor("hero")!.GetResource("health")!.Current);
        var projection = fixture.Projection.Project(request.Run!, request.Combat, "hero");
        Assert.True(projection.IsSuccess, projection.IsFailure ? projection.Error : null);
        Assert.Equal(0, projection.Value.Attributes.Single(value => value.AttributeId == "critical_damage").EffectiveValue);
        Assert.Equal(chance, projection.Value.Attributes.Single(value => value.AttributeId == "critical_chance").EffectiveValue);
        Assert.Equal(before, CanonicalJson.ComputeHash(request));
    }

    [Fact]
    public async Task BloodCovenantUsesPerHitPostArmorBlockAndOverkillAndCannotHealFromSelfDamage()
    {
        var fixture = await HeroAttributeScalingTests.Fixture(100, 200);
        var request = Health(fixture.Request);
        var enemy = request.Combat.GetActor("enemy")!;
        enemy = enemy.WithResourceState(enemy.ResourceState with { Resources = enemy.ResourceState.Resources.ToImmutableDictionary()
            .SetItem("health", enemy.GetResource("health")!.Set(35)).Add("block", Pool("block", 5, 100)) });
        request = Equip(request with { Combat = request.Combat.ReplaceActor(enemy) },
            fixture.Runtime.GetDefinition<RelicDefinition>("relics", "w1_blood_covenant").Value);
        var damage = request.Trigger.Effects[0] with { FlatValue = 100, Repeat = 2 };
        var result = fixture.Executor.Execute(request with { Trigger = request.Trigger with { Effects = [damage] } });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(27, result.Value.State.GetActor("hero")!.GetResource("health")!.Current);
        Assert.Equal(0, result.Value.State.GetActor("enemy")!.GetResource("health")!.Current);
        Assert.Single(result.Value.Records, record => record.Provenance.Kind == EffectProvenanceKind.Relic);
        var self = fixture.Executor.Execute(request with { SelectedTargetEntityIds = ["hero"] });
        Assert.True(self.IsSuccess, self.IsFailure ? self.Error : null);
        Assert.DoesNotContain(self.Value.Steps, step => step.Provenance.Kind == EffectProvenanceKind.Relic);
        var dot = fixture.Executor.Execute(request with { Trigger = request.Trigger with { Effects = [damage with
            { Repeat = 1, CalculationChannel = "w1_dot", CalculationPipelineId = "w1_dot", Tags = ["attack", "damage.residual"] }] } });
        Assert.True(dot.IsSuccess, dot.IsFailure ? dot.Error : null);
        Assert.DoesNotContain(dot.Value.Steps, step => step.Provenance.Kind == EffectProvenanceKind.Relic);
    }

    [Fact]
    public async Task IronVowDiscardsAndPreventsBlockAndUsesSameArmorInProjectionAndDamage()
    {
        var fixture = await HeroAttributeScalingTests.Fixture(0);
        var request = Health(fixture.Request);
        var hero = request.Combat.GetActor("hero")!;
        hero = hero.WithResourceState(hero.ResourceState with { Resources = hero.ResourceState.Resources.ToImmutableDictionary()
            .SetItem("health", hero.GetResource("health")!.Set(200)).Add("block", Pool("block", 10, 100)) });
        request = Equip(request with { Combat = request.Combat.ReplaceActor(hero) },
            fixture.Runtime.GetDefinition<RelicDefinition>("relics", "w1_iron_vow").Value);
        Assert.Equal(0, request.Combat.GetActor("hero")!.GetResource("block")!.Current);
        var projection = fixture.Projection.Project(request.Run!, request.Combat, "hero").Value;
        Assert.Equal(25, projection.Attributes.Single(value => value.AttributeId == "armor").EffectiveValue);
        Assert.Equal(20, projection.Attributes.Single(value => value.AttributeId == "armor_reduction").EffectiveValue, 3);
        var defend = new EffectDefinition { EffectId = "defend", Type = EffectType.MODIFY_RESOURCE,
            Target = EffectTarget.SELF, TargetResource = "block", FlatValue = 100 };
        var defended = fixture.Executor.Execute(request with { Trigger = request.Trigger with { Effects = [defend] } });
        Assert.True(defended.IsSuccess, defended.IsFailure ? defended.Error : null);
        Assert.Equal(0, defended.Value.State.GetActor("hero")!.GetResource("block")!.Current);
        var attacked = fixture.Executor.Execute(request with { SourceEntityId = "enemy", OwnerEntityId = "enemy", SelectedTargetEntityIds = ["hero"] });
        Assert.True(attacked.IsSuccess, attacked.IsFailure ? attacked.Error : null);
        Assert.Equal(80, attacked.Value.Steps[0].Calculation!.Value);
        Assert.Equal(120, attacked.Value.State.GetActor("hero")!.GetResource("health")!.Current);
        var removed = RelicTransitions.Remove(request.Run!, request.Run!.Relics[0].RelicInstanceId).Value;
        Assert.Null(removed.Encounters[0].Combat.GetActor("hero")!.GetResource("block")!.CurrentCeiling);
        Assert.Equal(0, removed.Encounters[0].Combat.GetActor("hero")!.GetResource("block")!.Current);
        Assert.Equal(0, fixture.Projection.Project(removed, removed.Encounters[0].Combat, "hero").Value.Attributes
            .Single(value => value.AttributeId == "armor").EffectiveValue);
    }

    private static EffectTriggerExecutionRequest Health(EffectTriggerExecutionRequest request)
    {
        var combat = request.Combat;
        foreach (var id in new[] { "hero", "enemy" })
        {
            var actor = combat.GetActor(id)!;
            combat = combat.ReplaceActor(actor.WithResourceState(actor.ResourceState with { Resources = new Dictionary<string, ResourcePool>
            { ["health"] = Pool("health", id == "hero" ? 20 : 2000, id == "hero" ? 200 : 2000) } }));
        }
        return request with { Combat = combat, Trigger = request.Trigger with { Effects = [request.Trigger.Effects[0] with
            { TargetResource = "health", Tags = ["attack"] }] } };
    }
    private static ResourcePool Pool(string id, float current, float maximum) => ResourcePool.Materialize(new()
        { ResourceId = id, DisplayName = id, DefaultMax = maximum }, current);
    private static EffectTriggerExecutionRequest Equip(EffectTriggerExecutionRequest request, RelicDefinition definition)
    {
        var acquired = RelicTransitions.Acquire(request.Run! with { Encounters = [new() { Combat = request.Combat }] },
            definition, new() { Kind = GameplayOwnerKind.Entity, Id = "hero" });
        Assert.True(acquired.IsSuccess, acquired.IsFailure ? acquired.Error : null);
        return request with { Run = acquired.Value.State, Combat = acquired.Value.State.Encounters[0].Combat };
    }
}
