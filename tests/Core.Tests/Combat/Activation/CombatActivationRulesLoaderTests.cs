using Core.Combat.Activation;
using Core.Config;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Combat.Activation;

public sealed class CombatActivationRulesLoaderTests
{
    private readonly Mock<IConfigManager> _configManager = new();
    private readonly Mock<IResourceLoader> _resourceLoader = new();

    [Fact]
    public void Load_LoadsActivationRulesFromResourceLoader()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "base", "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("combat-turn-rules/default_activation.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["default_activation"] = JsonDocument.Parse(RulesJson).RootElement.GetProperty("default_activation").Clone()
            });
        var loader = new CombatActivationRulesLoader(_configManager.Object, _resourceLoader.Object);

        var result = loader.Load("test", "default_activation");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("default_activation", result.Value.RulesId);
        Assert.Equal("existing_turn_order", result.Value.TurnOrderSource);
        Assert.Equal(2, result.Value.StartActivation.DrawCount);
        Assert.Equal(ActivationDiscardPolicy.DiscardNonRetain, result.Value.EndActivation.DiscardPolicy);
        Assert.Equal(UnknownCardPolicy.Fail, result.Value.EndActivation.UnknownCardPolicy);
        Assert.True(result.Value.Ai.AutoEndAfterAction);
    }

    [Fact]
    public void Load_MissingRules_ReturnsFailure()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("combat-turn-rules/missing.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>());
        var loader = new CombatActivationRulesLoader(_configManager.Object, _resourceLoader.Object);

        var result = loader.Load("test", "missing");

        Assert.True(result.IsFailure);
        Assert.Contains("Activation rules not found", result.Error);
    }

    private const string RulesJson = """
    {
      "default_activation": {
        "rulesId": "default_activation",
        "turnOrderSource": "existing_turn_order",
        "skipDeadActors": true,
        "startActivation": { "drawCount": 2 },
        "endActivation": {
          "discardPolicy": "DiscardNonRetain",
          "handLimit": 5,
          "retainTags": ["retain"],
          "unknownCardPolicy": "Fail"
        },
        "ai": { "autoProcess": false, "autoEndAfterAction": true },
        "events": { "emitActivationEvents": true, "emitDeckEvents": true }
      }
    }
    """;
}
