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

    [Fact]
    public void PickCards_AddsRewardToDiscardPile()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var selection = manager.CreateCardSelection(run.RunId, "basic_reward");
        var pick = manager.PickCards(run.RunId, selection.Value.SelectionInstanceId, new[] { "zap" });

        Assert.True(selection.IsSuccess, selection.IsFailure ? selection.Error : null);
        Assert.True(pick.IsSuccess, pick.IsFailure ? pick.Error : null);
        Assert.True(pick.Value.Completed);
        Assert.Contains("zap", run.Deck.DiscardPile);
    }

    [Fact]
    public void BuyShopItem_SpendsGoldAndAddsCardToDiscardPile()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var shop = manager.CreateShop(run.RunId, "basic_shop");
        var item = manager.BuyShopItem(run.RunId, shop.Value.ShopInstanceId, "buy_zap");

        Assert.True(shop.IsSuccess, shop.IsFailure ? shop.Error : null);
        Assert.True(item.IsSuccess, item.IsFailure ? item.Error : null);
        Assert.True(item.Value.Purchased);
        Assert.Equal(15, run.Gold);
        Assert.Contains("zap", run.Deck.DiscardPile);
    }

    [Fact]
    public void ApplyPreparationOption_SpendsResourcesAndAddsConfiguredCards()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation");
        var option = manager.ApplyPreparationOption(run.RunId, preparation.Value.PreparationInstanceId, "pack_supplies");

        Assert.True(preparation.IsSuccess, preparation.IsFailure ? preparation.Error : null);
        Assert.True(option.IsSuccess, option.IsFailure ? option.Error : null);
        Assert.True(option.Value.Applied);
        Assert.Equal(20, run.Gold);
        Assert.Contains("heal", run.Deck.DiscardPile);
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
        _resourceLoader
            .Setup(m => m.LoadResource("card-selections/basic_reward.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["basic_reward"] = JsonDocument.Parse(CardSelectionJson).RootElement.GetProperty("basic_reward").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("shops/basic_shop.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["basic_shop"] = JsonDocument.Parse(ShopJson).RootElement.GetProperty("basic_shop").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("preparations/basic_preparation.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["basic_preparation"] = JsonDocument.Parse(PreparationJson).RootElement.GetProperty("basic_preparation").Clone()
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

    private const string CardSelectionJson = """
    {
      "basic_reward": {
        "selectionId": "basic_reward",
        "pickCount": 1,
        "cardPool": ["strike", "defend", "zap"]
      }
    }
    """;

    private const string ShopJson = """
    {
      "basic_shop": {
        "shopId": "basic_shop",
        "items": [
          { "itemId": "buy_zap", "cardId": "zap", "goldCost": 10, "powerPointCost": 0 }
        ]
      }
    }
    """;

    private const string PreparationJson = """
    {
      "basic_preparation": {
        "preparationId": "basic_preparation",
        "options": [
          { "optionId": "pack_supplies", "goldCost": 5, "powerPointCost": 0, "addCardsToDiscard": ["heal"] }
        ]
      }
    }
    """;
}
