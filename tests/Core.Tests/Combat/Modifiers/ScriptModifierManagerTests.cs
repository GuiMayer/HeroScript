using Core.Combat.Modifiers;
using Core.Config;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat.Modifiers;

public sealed class ScriptModifierManagerTests : IDisposable
{
    private readonly ScriptModifierManager _manager;

    public ScriptModifierManagerTests()
    {
        var configManager = new Mock<IConfigManager>();
        var resourceLoader = new Mock<IResourceLoader>();
        configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        resourceLoader
            .Setup(m => m.LoadResource("Modifiers/script_modifiers.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(TestDefinitionsJson));

        _manager = new ScriptModifierManager(configManager.Object, resourceLoader.Object);
        var load = _manager.LoadDefinitions("test");
        Assert.True(load.IsSuccess, load.IsFailure ? load.Error : null);
    }

    [Fact]
    public void LoadDefinitions_LoadsJsonDefinitions()
    {
        var definition = _manager.GetDefinition("glass_cannon");

        Assert.True(definition.IsSuccess, definition.IsFailure ? definition.Error : null);
        Assert.Equal("increased_damage_total", definition.Value.ModifierKey);
        Assert.Contains("attack", definition.Value.RequiredTags);
    }

    [Fact]
    public void ApplyModifier_WithExistingModifier_StacksAndClamps()
    {
        var first = _manager.ApplyModifier("run-1", "glass_cannon", stacks: 2);
        var second = _manager.ApplyModifier("run-1", "glass_cannon", stacks: 5);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error : null);
        Assert.Equal(first.Value.InstanceId, second.Value.InstanceId);
        Assert.Equal(4, second.Value.Stacks);
        Assert.Single(_manager.GetActiveModifiers("run-1"));
    }

    [Fact]
    public void GetPipelineModifiers_FiltersByTagsAndAccumulatesValues()
    {
        _manager.ApplyModifier("run-1", "glass_cannon", stacks: 2);
        _manager.ApplyModifier("run-1", "flat_bonus", stacks: 1);

        var attackModifiers = _manager.GetPipelineModifiers("run-1", new[] { "attack" });
        var skillModifiers = _manager.GetPipelineModifiers("run-1", new[] { "skill" });

        Assert.Equal(0.5f, attackModifiers["increased_damage_total"]);
        Assert.Equal(3f, attackModifiers["added_damage"]);
        Assert.False(skillModifiers.ContainsKey("increased_damage_total"));
        Assert.Equal(3f, skillModifiers["added_damage"]);
    }

    [Fact]
    public void TickDurations_RemovesExpiredFiniteModifiers()
    {
        _manager.ApplyModifier("run-1", "temporary_focus", duration: 1);

        var tick = _manager.TickDurations("run-1");

        Assert.True(tick.IsSuccess, tick.IsFailure ? tick.Error : null);
        Assert.Empty(_manager.GetActiveModifiers("run-1"));
    }

    public void Dispose()
    {
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private const string TestDefinitionsJson = """
    {
      "glass_cannon": {
        "modifierId": "glass_cannon",
        "displayName": "Glass Cannon",
        "description": "Attacks deal more damage.",
        "modifierKey": "increased_damage_total",
        "formulaValue": "stacks * 0.25",
        "baseValue": 0.25,
        "defaultStacks": 1,
        "maxStacks": 4,
        "defaultDuration": -1,
        "requiredTags": ["attack"],
        "tags": ["damage"]
      },
      "flat_bonus": {
        "modifierId": "flat_bonus",
        "displayName": "Flat Bonus",
        "description": "Adds flat damage.",
        "modifierKey": "added_damage",
        "baseValue": 3.0,
        "defaultDuration": -1
      },
      "temporary_focus": {
        "modifierId": "temporary_focus",
        "displayName": "Temporary Focus",
        "description": "Temporary damage bonus.",
        "modifierKey": "increased_damage_total",
        "baseValue": 0.1,
        "defaultDuration": 1
      }
    }
    """;
}
