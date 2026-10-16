using System.Collections.Immutable;
using Core.Determinism;
using Core.Effects;
using Core.Run;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.Content;

public sealed class TagSynergyContractsTests
{
    [Fact]
    public async Task UtilityChannel_IgnoresOffensiveCriticalArmorAndBlock()
    {
        var f = await TagSynergyExecutionFixture.Create(energy: 1, block: 20, critical: 250, armor: 10000);
        var before = CanonicalJson.ComputeHash(f.Combat);
        var result = f.Executor.Execute(new() { Run = f.Run, Combat = f.Combat,
            OwnerEntityId = "hero", SourceEntityId = "hero", ContentRevision = f.Runtime.Manifest.Revision,
            Tags = ImmutableHashSet.Create("damage", "attack", "fire"), Trigger = new() { TriggerId = "utility",
                Effects = [new() { Type = EffectType.MODIFY_RESOURCE, Target = EffectTarget.SELF,
                    TargetResource = "energy", CalculationChannel = "w1_utility",
                    CalculationPipelineId = "w1_utility", Tags = ["resource_gain"],
                    Parameters = [new() { Parameter = EffectNumericParameter.Amount, FlatValue = 2,
                        UnitId = "points", Channel = "w1_utility", PipelineId = "w1_utility" }] }] } });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal(3, TagSynergyExecutionFixture.Value(result.Value.State, "energy"));
        Assert.Equal(20, TagSynergyExecutionFixture.Value(result.Value.State, "block"));
        Assert.Equal(before, CanonicalJson.ComputeHash(f.Combat));
    }

    [Fact]
    public async Task TagRecipe_AcceptsNovelAuthorizedStatusButNotUnauthorizedOrOtherUnits()
    {
        var f = await TagSynergyExecutionFixture.Create();
        var source = f.Runtime.GetDefinition<StatusEffectDefinition>("status-effects", "w1_burn").Value;
        var recipe = f.Runtime.GetDefinition<CondensationRecipeDefinition>("condensation-recipes", "w1_release_dot").Value;
        Assert.Empty(recipe.Selection.DefinitionIds);
        StatusEffectInstance Status(string id, StatusEffectDefinition definition, int n) => new() {
            InstanceId = new Guid(n, 0, 0, new byte[8]), StatusId = id, TargetId = "enemy",
            SourceId = "hero", Stacks = 1, Duration = 3, ContentRevision = f.Runtime.Manifest.Revision, Definition = definition };
        var state = f.Combat with { StatusEffects = f.Combat.StatusEffects.SetItem("enemy", [
            Status("novel", source with { StatusId = "novel" }, 1),
            Status("unauthorized", source with { StatusId = "unauthorized", Consumption = new() }, 2),
            Status("healing", source with { StatusId = "healing", Tags = ["residual", "healing_payload"] }, 3)]) };
        var before = CanonicalJson.ComputeHash(state);
        var selected = AccumulatedStackTransitions.Capture(state, f.Run, recipe.RecipeId, recipe.Selection);
        Assert.True(selected.IsSuccess, selected.IsFailure ? selected.Error : null);
        Assert.Equal("novel", Assert.Single(selected.Value.Sources).DefinitionId);
        Assert.Equal(before, CanonicalJson.ComputeHash(state));
    }
}
