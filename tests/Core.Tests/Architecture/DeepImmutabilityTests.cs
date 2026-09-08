using Core.Combat.Gambits;
using Core.Combat.Modifiers;
using Core.Calculations;
using Core.Entity.Definitions;
using Core.Resources;
using Core.Run.Sandbox;
using Xunit;

namespace Core.Tests.Architecture;

public sealed class DeepImmutabilityTests
{
    [Theory]
    [InlineData(typeof(EntityDefinition))]
    [InlineData(typeof(ResourceEntityComponentDefinition))]
    [InlineData(typeof(StatEntityComponentDefinition))]
    [InlineData(typeof(Core.Combat.Models.CombatActorState))]
    [InlineData(typeof(Core.Combat.Models.StatEntityComponentState))]
    [InlineData(typeof(Core.Combat.Gambits.GambitDefinition))]
    [InlineData(typeof(ScriptModifierDefinition))]
    [InlineData(typeof(ResourceDefinition))]
    [InlineData(typeof(CalculationPipelineDefinition))]
    [InlineData(typeof(CalculationBucketDefinition))]
    [InlineData(typeof(SandboxCombatSnapshot))]
    public void SharedModels_DoNotExposeMutableCollectionTypes(Type modelType)
    {
        var mutableCollectionProperties = modelType.GetProperties()
            .Where(property => IsMutableCollection(property.PropertyType))
            .Select(property => property.Name)
            .ToArray();

        Assert.True(
            mutableCollectionProperties.Length == 0,
            $"{modelType.Name} exposes mutable collections: {string.Join(", ", mutableCollectionProperties)}");
    }

    [Fact]
    public void EntityDefinition_DefensivelyCopiesNestedCollections()
    {
        var customStats = new Dictionary<string, float> { ["luck"] = 10f };
        var metadata = new Dictionary<string, object> { ["faction"] = "red" };
        var definition = new EntityDefinition
        {
            Components = [new StatEntityComponentDefinition { ComponentId = "stats", Values = customStats }],
            Metadata = metadata
        };

        customStats["luck"] = 999f;
        metadata["faction"] = "blue";

        Assert.Equal(10f, definition.Component<StatEntityComponentDefinition>()!.Values["luck"]);
        Assert.Equal("red", definition.Metadata["faction"]);
    }

    [Fact]
    public void CalculationPipeline_DefensivelyCopiesNestedCollections()
    {
        var buckets = new List<CalculationBucketDefinition>
        {
            new() { BucketId = "base", Order = 1, Operation = CalculationBucketOperation.Add }
        };
        var pipeline = new CalculationPipelineDefinition { PipelineId = "test", Channel = "amount", Buckets = buckets };

        buckets.Clear();

        Assert.Single(pipeline.Buckets);
        Assert.Equal(CalculationBucketOperation.Add, pipeline.Buckets[0].Operation);
    }

    private static bool IsMutableCollection(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) ||
               definition == typeof(Dictionary<,>) ||
               definition == typeof(HashSet<>);
    }
}
