using Core.Config;
using Core.Run;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Run;

public sealed class RunManagerTests
{
    private readonly Mock<IConfigManager> _configManager = new();
    private readonly Mock<IResourceLoader> _resourceLoader = new();

    [Fact]
    public void StartRun_LoadsDefinitionFromJsonAndDrawsStartingHand()
    {
        var manager = CreateManager();

        var result = manager.StartRun("test", "default_run", "hero");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("test", result.Value.ConfigName);
        Assert.Equal("hero", result.Value.PlayerEntityId);
        Assert.Equal(25, result.Value.Gold);
        Assert.Equal(new[] { "strike", "defend" }, result.Value.Deck.Hand);
        Assert.Equal(new[] { "zap" }, result.Value.Deck.DrawPile);
        Assert.Equal("start", result.Value.CurrentNodeId);
    }

    [Fact]
    public void DrawCards_ShufflesDiscardWhenDrawPileIsEmpty()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var discard = manager.DiscardCards(run.RunId, run.Deck.Hand.ToArray());
        Assert.True(discard.IsSuccess, discard.IsFailure ? discard.Error : null);

        var drawn = manager.DrawCards(run.RunId, 3);

        Assert.True(drawn.IsSuccess, drawn.IsFailure ? drawn.Error : null);
        Assert.Equal(new[] { "zap", "strike", "defend" }, drawn.Value);
        Assert.Equal(3, run.Deck.Hand.Count);
    }

    [Fact]
    public void ApplyEconomy_UpdatesGoldAndPowerPoints()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var gold = manager.ApplyEconomy(run.RunId, "gold", -10);
        var pp = manager.ApplyEconomy(run.RunId, "pp", 3);

        Assert.True(gold.IsSuccess, gold.IsFailure ? gold.Error : null);
        Assert.True(pp.IsSuccess, pp.IsFailure ? pp.Error : null);
        Assert.Equal(15, run.Gold);
        Assert.Equal(3, run.PowerPoints);
    }

    private RunManager CreateManager()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("runs/default_run.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["default_run"] = JsonDocument.Parse(RunJson).RootElement.GetProperty("default_run").Clone()
            });

        return new RunManager(_configManager.Object, _resourceLoader.Object);
    }

    private const string RunJson = """
    {
      "default_run": {
        "runId": "default_run",
        "startingGold": 25,
        "startingPowerPoints": 0,
        "startingHandSize": 2,
        "startingDeck": ["strike", "defend", "zap"],
        "mapNodes": [
          { "nodeId": "start", "nodeType": "combat", "nextNodeIds": [] }
        ]
      }
    }
    """;
}
