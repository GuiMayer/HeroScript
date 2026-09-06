using Core.Calculations;
using Core.Combat.Flow;
using Core.Effects;
using Core.StatusEffects;
using Xunit;

namespace Core.Tests.StatusEffects;

[Trait("Category", "Unit")]
public sealed class StatusEffectsModelsTests
{
    [Fact]
    public void DefinitionIsAComponentContainerWithDefensiveCollections()
    {
        var tags = new List<string> { "fire", "debuff" };
        var properties = new Dictionary<string, object> { ["uiGroup"] = "negative" };
        var influences = new List<ContextualInfluenceDefinition>
        {
            new() { InfluenceId = "burning.more", Channel = "effect_amount", Bucket = "more", Value = .1f }
        };
        var triggers = new List<EffectTriggerDefinition>
        {
            new()
            {
                TriggerId = "burning.tick",
                Boundary = StatusTriggerBoundary.EndActivation.ToString(),
                Effects =
                [
                    new()
                    {
                        EffectId = "burning.health",
                        Type = EffectType.DAMAGE,
                        Target = EffectTarget.SELF,
                        TargetResource = "health",
                        FormulaValue = "stacks * 3"
                    }
                ]
            }
        };
        var definition = new StatusEffectDefinition
        {
            StatusId = "burning",
            DisplayName = "Burning",
            Description = "Configured entirely through components",
            DefaultDuration = 3,
            DefaultStacks = 1,
            MaxStacks = 10,
            Stacking = StackReapplyPolicy.Add,
            DurationReapply = DurationReapplyPolicy.Refresh,
            DurationTickBoundary = StatusTriggerBoundary.EndActivation,
            ActionConstraints = [new() { ConstraintId = "cannot-cast", RequiredActionTags = ["spell"] }],
            Influences = influences,
            Triggers = triggers,
            Tags = tags,
            CustomData = properties
        };

        tags.Add("caller-mutation");
        influences.Clear();
        triggers.Clear();
        properties["uiGroup"] = "changed";

        Assert.Equal(new[] { "fire", "debuff" }, definition.Tags.ToArray());
        Assert.Single(definition.Influences);
        Assert.Single(definition.Triggers);
        Assert.Single(definition.ActionConstraints);
        Assert.Equal("negative", definition.CustomData["uiGroup"]);
    }

    [Fact]
    public void DefinitionDoesNotExposeParallelBehaviorOrNumericRules()
    {
        var propertyNames = typeof(StatusEffectDefinition).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "Type", "Behavior", "Timing", "TriggerBoundary", "BaseValue",
                     "FormulaValue", "ScalesWithStacks", "TargetResource", "ModifierKey", "ModifierFormula"
                 })
            Assert.DoesNotContain(forbidden, propertyNames);
    }

    [Fact]
    public void InstancePinsDefinitionRevisionAndUsesDeterministicSentinels()
    {
        var definition = new StatusEffectDefinition { StatusId = "poison" };
        var instance = new StatusEffectInstance
        {
            InstanceId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            StatusId = "poison",
            Definition = definition,
            TargetId = "enemy",
            SourceId = "hero",
            ContentRevision = "revision",
            Stacks = 3,
            Duration = 5,
            AppliedAt = DateTime.UnixEpoch.AddSeconds(7),
            TurnApplied = 2
        };

        Assert.Same(definition, instance.Definition);
        Assert.Equal("revision", instance.ContentRevision);
        Assert.Equal(3, instance.Stacks);
        Assert.Equal(5, instance.Duration);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(7), instance.AppliedAt);
        Assert.Equal(DateTime.UnixEpoch, new StatusEffectInstance().AppliedAt);
    }

    [Fact]
    public void InstanceMutationCreatesANewSnapshotAndCopiesCustomData()
    {
        var custom = new Dictionary<string, object> { ["absorbed"] = 4 };
        var instance = new StatusEffectInstance
        {
            Stacks = 1,
            Duration = 3,
            CustomData = custom
        };
        custom["absorbed"] = 99;

        var changed = instance with { Stacks = 2, Duration = 2 };

        Assert.Equal(1, instance.Stacks);
        Assert.Equal(3, instance.Duration);
        Assert.Equal(4, instance.CustomData["absorbed"]);
        Assert.Equal(2, changed.Stacks);
        Assert.Equal(2, changed.Duration);
    }
}
