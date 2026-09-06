using Core.Effects;
using Core.Resources;
using Xunit;

namespace Core.Tests.Effects;

[Trait("Category", "Unit")]
public sealed class EffectsModelsTests
{
    [Fact]
    public void EffectTypeContainsOnlyExecutablePrimitives()
    {
        Assert.Equal(
            new[]
            {
                EffectType.DAMAGE,
                EffectType.HEAL,
                EffectType.MODIFY_RESOURCE,
                EffectType.APPLY_STATUS,
                EffectType.REMOVE_STATUS,
                EffectType.DISPEL_STATUS,
                EffectType.DRAW_CARD,
                EffectType.DISCARD_CARD,
                EffectType.EXHAUST_CARD,
                EffectType.ADD_CARD_TO_HAND,
                EffectType.APPLY_MODIFIER,
                EffectType.REMOVE_MODIFIER
            },
            Enum.GetValues<EffectType>());
    }

    [Fact]
    public void DefinitionCopiesCallerOwnedCollections()
    {
        var requiredTags = new List<string> { "attack" };
        var tags = new List<string> { "fire" };
        var metadata = new Dictionary<string, object> { ["presentation"] = "burst" };
        var children = new List<EffectDefinition>
        {
            new() { EffectId = "ignite", Type = EffectType.APPLY_STATUS, StatusId = "burning" }
        };
        var definition = new EffectDefinition
        {
            EffectId = "fireball.damage",
            Type = EffectType.DAMAGE,
            Target = EffectTarget.TARGET,
            TargetResource = "health",
            ResourceField = ResourceValueField.Current,
            FlatValue = 8,
            RequiredTags = requiredTags,
            Tags = tags,
            Metadata = metadata,
            ChainedEffects = children
        };

        requiredTags.Add("mutated");
        tags.Clear();
        metadata["presentation"] = "changed";
        children.Clear();

        Assert.Equal(new[] { "attack" }, definition.RequiredTags);
        Assert.Equal(new[] { "fire" }, definition.Tags);
        Assert.Equal("burst", definition.Metadata["presentation"]);
        Assert.Single(definition.ChainedEffects!);
    }

    [Fact]
    public void DefinitionDoesNotExposeRejectedLegacyAuthoringFields()
    {
        var propertyNames = typeof(EffectDefinition).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "Timing", "IsPercentage", "ModifierKey", "ModifierValue",
                     "ModifierFormula", "ConditionalEffects"
                 })
            Assert.DoesNotContain(forbidden, propertyNames);
    }

    [Fact]
    public void LegacyMutableEffectRuntimeModelsAreAbsent()
    {
        var assembly = typeof(EffectDefinition).Assembly;
        foreach (var typeName in new[]
                 {
                     "EffectInstance", "EffectResult", "EffectApplicationResult",
                     "EffectModifier", "IEffectContext", "EffectScope", "EffectExecutionState"
                 })
            Assert.Null(assembly.GetType($"Core.Effects.{typeName}"));
    }

    [Fact]
    public void TriggerCopiesEffectsAndKeepsBoundaryAsOwnerData()
    {
        var effects = new List<EffectDefinition>
        {
            new() { EffectId = "tick", Type = EffectType.DAMAGE, TargetResource = "stability" }
        };
        var trigger = new EffectTriggerDefinition
        {
            TriggerId = "status.tick",
            Boundary = "EndActivation",
            Priority = 10,
            Effects = effects
        };
        effects.Clear();

        Assert.Equal("EndActivation", trigger.Boundary);
        Assert.Single(trigger.Effects);
    }
}
