using System.Text.Json;
using Core.Combat.Models;
using Core.Determinism;
using Core.Entity.Definitions;
using Core.Entity.Integration;
using Xunit;

namespace Core.Tests.Entity.Integration;

public sealed class EntityMaterializerTests
{
    [Fact]
    public void Materialize_ProducesTheSameImmutableStateForTheSamePinnedInput()
    {
        var stats = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["focus"] = 7
        };
        var abilities = new List<string> { "pulse" };
        var definition = new EntityDefinition
        {
            DefinitionId = "generic_unit",
            DisplayName = "Generic Unit",
            Components =
            [
                new StatEntityComponentDefinition { ComponentId = "stats", Values = stats },
                new AbilityEntityComponentDefinition { ComponentId = "abilities", AbilityIds = abilities }
            ]
        };
        var materializer = new EntityMaterializer(TestDataBuilders.MockResourceManager().Object);
        var binding = new ControllerBinding { Kind = ControllerKind.Player };

        stats["focus"] = 99;
        abilities.Add("caller_mutation");
        var first = materializer.Materialize(definition, "unit-1", "revision-a", "alpha", binding);
        var second = materializer.Materialize(definition, "unit-1", "revision-a", "alpha", binding);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(CanonicalJson.ComputeHash(first.Value), CanonicalJson.ComputeHash(second.Value));
        Assert.Equal(7, first.Value.Component<StatEntityComponentState>()!.Values["focus"]);
        Assert.Equal(["pulse"], first.Value.Component<AbilityEntityComponentState>()!.AbilityIds);
        Assert.Equal("generic_unit", first.Value.DefinitionId);
        Assert.Equal("revision-a", first.Value.ContentRevision);
    }

    [Fact]
    public void DefinitionDeserializer_RejectsUnknownComponentDiscriminator()
    {
        const string json = """
            {
              "definitionId": "invalid",
              "displayName": "Invalid",
              "components": [
                { "type": "unknownComponent", "componentId": "unknown" }
              ]
            }
            """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EntityDefinition>(json, options));
    }
}
