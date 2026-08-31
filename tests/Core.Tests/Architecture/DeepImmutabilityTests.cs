using Core.Combat.Gambits;
using Core.Combat.Modifiers;
using Core.Damage;
using Core.Entity.Definitions;
using Core.Resources;
using Core.Run.Sandbox;
using Xunit;

namespace Core.Tests.Architecture;

public sealed class DeepImmutabilityTests
{
    [Theory]
    [InlineData(typeof(EntityDefinition))]
    [InlineData(typeof(ResourcesDefinition))]
    [InlineData(typeof(StatsDefinition))]
    [InlineData(typeof(Core.Combat.Gambits.GambitDefinition))]
    [InlineData(typeof(ScriptModifierDefinition))]
    [InlineData(typeof(ResourceDefinition))]
    [InlineData(typeof(PipelineConfiguration))]
    [InlineData(typeof(BucketDefinition))]
    [InlineData(typeof(BucketOperation))]
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
        var customData = new Dictionary<string, object> { ["faction"] = "hero" };
        var definition = new EntityDefinition
        {
            Stats = new StatsDefinition { CustomStats = customStats },
            CustomData = customData
        };

        customStats["luck"] = 999f;
        customData["faction"] = "enemy";

        Assert.Equal(10f, definition.Stats!.CustomStats["luck"]);
        Assert.Equal("hero", definition.CustomData["faction"]);
    }

    [Fact]
    public void PipelineConfiguration_DefensivelyCopiesNestedCollections()
    {
        var parameters = new Dictionary<string, object> { ["amount"] = 5f };
        var operations = new List<BucketOperation>
        {
            new() { Type = OperationType.ADD_FLAT, Parameters = parameters }
        };
        var buckets = new List<BucketDefinition>
        {
            new() { BucketId = "base", Order = 1, Operations = operations }
        };
        var pipeline = new PipelineConfiguration { ConfigName = "test", Buckets = buckets };

        parameters["amount"] = 99f;
        operations.Clear();
        buckets.Clear();

        Assert.Single(pipeline.Buckets);
        Assert.Single(pipeline.Buckets[0].Operations);
        Assert.Equal(5f, pipeline.Buckets[0].Operations[0].Parameters["amount"]);
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
