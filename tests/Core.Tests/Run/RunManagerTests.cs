using Core.Config;
using Core.Combat.Modifiers;
using Core.Common;
using Core.Run;
using Core.Run.Content;
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
    public void CreateCardSelection_WithPool_GeneratesEnrichedOptions()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var selection = manager.CreateCardSelection(run.RunId, "pool_reward");

        Assert.True(selection.IsSuccess, selection.IsFailure ? selection.Error : null);
        Assert.Equal(2, selection.Value.Options.Count);
        Assert.Equal("basic_rewards", selection.Value.CardPoolId);
        Assert.Contains(selection.Value.Options, option => option.CardId == "heal" && option.Rarity == CardRarity.Common);
        Assert.Contains(selection.Value.Options, option => option.CardId == "fireball" && option.Rarity == CardRarity.Uncommon);
    }

    [Fact]
    public void RerollCardSelection_FreeReroll_ReplacesUnlockedOptionsWithoutSpendingGold()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;

        var reroll = manager.RerollCardSelection(run.RunId, selection.SelectionInstanceId, new[] { "heal" });

        Assert.True(reroll.IsSuccess, reroll.IsFailure ? reroll.Error : null);
        Assert.Equal(25, run.Gold);
        Assert.Equal(1, reroll.Value.RerollsUsed);
        Assert.Equal(0, reroll.Value.FreeRerollsRemaining);
        Assert.Contains(reroll.Value.Options, option => option.CardId == "heal");
    }

    [Fact]
    public void DecomposeCardSelectionOption_AddsPowerPointsAndBlocksPick()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;

        var decompose = manager.DecomposeCardSelectionOption(run.RunId, selection.SelectionInstanceId, "fireball");
        var pick = manager.PickCards(run.RunId, selection.SelectionInstanceId, new[] { "fireball" });

        Assert.True(decompose.IsSuccess, decompose.IsFailure ? decompose.Error : null);
        Assert.Equal(2, run.PowerPoints);
        Assert.Contains("fireball", decompose.Value.DecomposedCardIds);
        Assert.True(pick.IsFailure);
        Assert.Contains("Invalid card options", pick.Error);
    }

    [Fact]
    public void PickCards_WhenInvalidPick_DoesNotCompleteSelectionOrAddCards()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "basic_reward").Value;
        var originalDiscard = run.Deck.DiscardPile.ToArray();

        var pick = manager.PickCards(run.RunId, selection.SelectionInstanceId, new[] { "missing_card" });

        Assert.True(pick.IsFailure);
        Assert.False(selection.Completed);
        Assert.Empty(selection.PickedCardIds);
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
    }

    [Fact]
    public void DecomposeCardSelectionOption_WhenAlreadyDecomposed_DoesNotAddPowerPointsAgain()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;
        var first = manager.DecomposeCardSelectionOption(run.RunId, selection.SelectionInstanceId, "fireball");
        var powerPointsAfterFirst = run.PowerPoints;

        var second = manager.DecomposeCardSelectionOption(run.RunId, selection.SelectionInstanceId, "fireball");

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error : null);
        Assert.True(second.IsFailure);
        Assert.Equal(powerPointsAfterFirst, run.PowerPoints);
        Assert.Single(selection.DecomposedCardIds, id => id == "fireball");
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
    public void BuyShopItem_WithInsufficientResources_DoesNotMutateRunOrItem()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run.Gold = 0;
        var shop = manager.CreateShop(run.RunId, "basic_shop").Value;
        var originalDiscard = run.Deck.DiscardPile.ToArray();

        var item = manager.BuyShopItem(run.RunId, shop.ShopInstanceId, "buy_zap");

        Assert.True(item.IsFailure);
        Assert.Equal(0, run.Gold);
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.False(shop.Items.Single(i => i.ItemId == "buy_zap").Purchased);
    }

    [Fact]
    public void CreateShop_WithPool_GeneratesPricedItems()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var shop = manager.CreateShop(run.RunId, "dynamic_shop");

        Assert.True(shop.IsSuccess, shop.IsFailure ? shop.Error : null);
        Assert.Equal("basic_rewards", shop.Value.CardPoolId);
        Assert.Equal(2, shop.Value.Items.Count);
        Assert.Contains(shop.Value.Items, item => item.CardId == "heal" && item.GoldCost == 18);
        Assert.Contains(shop.Value.Items, item => item.CardId == "fireball" && item.GoldCost == 35);
        Assert.All(shop.Value.Items, item => Assert.True(item.PricingBreakdown.ContainsKey("final")));
    }

    [Fact]
    public void RerollShop_ChargesGoldAndRegeneratesItems()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var shop = manager.CreateShop(run.RunId, "dynamic_shop").Value;

        var reroll = manager.RerollShop(run.RunId, shop.ShopInstanceId);

        Assert.True(reroll.IsSuccess, reroll.IsFailure ? reroll.Error : null);
        Assert.Equal(15, run.Gold);
        Assert.Equal(1, reroll.Value.RerollsUsed);
        Assert.Equal(15, reroll.Value.RerollCostGold);
        Assert.All(reroll.Value.Items, item => Assert.False(item.Purchased));
    }

    [Fact]
    public void RerollShop_WithInsufficientGold_DoesNotMutateShop()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var shop = manager.CreateShop(run.RunId, "dynamic_shop").Value;
        run.Gold = 0;
        var originalItems = shop.Items.Select(i => i.ItemId).ToArray();
        var originalRerolls = shop.RerollsUsed;
        var originalCost = shop.RerollCostGold;

        var reroll = manager.RerollShop(run.RunId, shop.ShopInstanceId);

        Assert.True(reroll.IsFailure);
        Assert.Equal(0, run.Gold);
        Assert.Equal(originalRerolls, shop.RerollsUsed);
        Assert.Equal(originalCost, shop.RerollCostGold);
        Assert.Equal(originalItems, shop.Items.Select(i => i.ItemId));
    }

    [Fact]
    public void RerollCardSelection_WithInsufficientGold_DoesNotMutateSelection()
    {
        var manager = CreateManagerWithContent();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        var selection = manager.CreateCardSelection(run.RunId, "pool_reward").Value;
        var free = manager.RerollCardSelection(run.RunId, selection.SelectionInstanceId);
        Assert.True(free.IsSuccess, free.IsFailure ? free.Error : null);
        run.Gold = 0;
        var originalOptions = selection.Options.Select(o => o.CardId).ToArray();
        var originalRerolls = selection.RerollsUsed;
        var originalFree = selection.FreeRerollsRemaining;
        var originalCost = selection.RerollCostGold;

        var paid = manager.RerollCardSelection(run.RunId, selection.SelectionInstanceId);

        Assert.True(paid.IsFailure);
        Assert.Equal(0, run.Gold);
        Assert.Equal(originalRerolls, selection.RerollsUsed);
        Assert.Equal(originalFree, selection.FreeRerollsRemaining);
        Assert.Equal(originalCost, selection.RerollCostGold);
        Assert.Equal(originalOptions, selection.Options.Select(o => o.CardId));
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

    [Fact]
    public void ApplyPreparationOption_AppliesConfiguredScriptModifiers()
    {
        var modifierManager = new Mock<IScriptModifierManager>();
        var manager = CreateManager(scriptModifierManager: modifierManager.Object);
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run.PowerPoints = 2;
        var instanceId = Guid.NewGuid();
        modifierManager
            .Setup(m => m.ApplyModifier($"run:{run.RunId}", "flat_power_bonus", 1, -1, "train_spell"))
            .Returns(Result<ScriptModifierInstance>.Success(new ScriptModifierInstance
            {
                InstanceId = instanceId,
                ModifierId = "flat_power_bonus",
                OwnerId = $"run:{run.RunId}",
                SourceId = "train_spell"
            }));

        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;
        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "train_spell");

        Assert.True(option.IsSuccess, option.IsFailure ? option.Error : null);
        Assert.True(option.Value.Applied);
        Assert.Equal(1, run.PowerPoints);
        Assert.Contains("fireball", run.Deck.DiscardPile);
        Assert.Contains(instanceId, option.Value.AppliedModifierInstanceIds);
        modifierManager.Verify(m => m.ApplyModifier($"run:{run.RunId}", "flat_power_bonus", 1, -1, "train_spell"), Times.Once);
    }

    [Fact]
    public void ApplyPreparationOption_WhenModifierManagerMissing_RollsBackResourcesAndCards()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run.PowerPoints = 2;
        var originalGold = run.Gold;
        var originalPowerPoints = run.PowerPoints;
        var originalDiscard = run.Deck.DiscardPile.ToArray();
        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;

        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "train_spell");

        Assert.True(option.IsFailure);
        Assert.Equal(originalGold, run.Gold);
        Assert.Equal(originalPowerPoints, run.PowerPoints);
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.Empty(preparation.AppliedOptionIds);
        Assert.False(preparation.Options.Single(o => o.OptionId == "train_spell").Applied);
    }

    [Fact]
    public void ApplyPreparationOption_WhenModifierApplyFails_RollsBackResourcesCardsAndAppliedState()
    {
        var modifierManager = new Mock<IScriptModifierManager>();
        var manager = CreateManager(scriptModifierManager: modifierManager.Object);
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run.PowerPoints = 2;
        var originalGold = run.Gold;
        var originalPowerPoints = run.PowerPoints;
        var originalDiscard = run.Deck.DiscardPile.ToArray();
        modifierManager
            .Setup(m => m.ApplyModifier($"run:{run.RunId}", "flat_power_bonus", 1, -1, "train_spell"))
            .Returns(Result<ScriptModifierInstance>.Failure("modifier rejected"));
        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;

        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "train_spell");

        Assert.True(option.IsFailure);
        Assert.Equal("modifier rejected", option.Error);
        Assert.Equal(originalGold, run.Gold);
        Assert.Equal(originalPowerPoints, run.PowerPoints);
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.Empty(preparation.AppliedOptionIds);
        Assert.False(preparation.Options.Single(o => o.OptionId == "train_spell").Applied);
        Assert.Empty(preparation.Options.Single(o => o.OptionId == "train_spell").AppliedModifierInstanceIds);
    }

    [Fact]
    public void ApplyPreparationOption_WhenSecondModifierFails_RemovesFirstModifierAndRollsBackRunState()
    {
        var modifierManager = new Mock<IScriptModifierManager>();
        var manager = CreateManager(scriptModifierManager: modifierManager.Object);
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run.PowerPoints = 3;
        var firstInstanceId = Guid.NewGuid();
        var originalGold = run.Gold;
        var originalPowerPoints = run.PowerPoints;
        var originalDiscard = run.Deck.DiscardPile.ToArray();
        modifierManager
            .Setup(m => m.ApplyModifier($"run:{run.RunId}", "flat_power_bonus", 1, -1, "double_train"))
            .Returns(Result<ScriptModifierInstance>.Success(new ScriptModifierInstance
            {
                InstanceId = firstInstanceId,
                ModifierId = "flat_power_bonus",
                OwnerId = $"run:{run.RunId}",
                SourceId = "double_train"
            }));
        modifierManager
            .Setup(m => m.ApplyModifier($"run:{run.RunId}", "missing_modifier", 1, -1, "double_train"))
            .Returns(Result<ScriptModifierInstance>.Failure("modifier missing"));
        modifierManager
            .Setup(m => m.RemoveModifier($"run:{run.RunId}", firstInstanceId))
            .Returns(Result.Success());
        var preparation = manager.CreatePreparation(run.RunId, "basic_preparation").Value;

        var option = manager.ApplyPreparationOption(run.RunId, preparation.PreparationInstanceId, "double_train");

        Assert.True(option.IsFailure);
        Assert.Equal("modifier missing", option.Error);
        Assert.Equal(originalGold, run.Gold);
        Assert.Equal(originalPowerPoints, run.PowerPoints);
        Assert.Equal(originalDiscard, run.Deck.DiscardPile);
        Assert.Empty(preparation.AppliedOptionIds);
        Assert.False(preparation.Options.Single(o => o.OptionId == "double_train").Applied);
        modifierManager.Verify(m => m.RemoveModifier($"run:{run.RunId}", firstInstanceId), Times.Once);
    }

    [Fact]
    public void ConsumeCardsFromHand_Discard_RemovesFromHandAndAddsToDiscard()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var result = manager.ConsumeCardsFromHand(run.RunId, new[] { "strike" }, CardConsumeDestination.Discard);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.DoesNotContain("strike", run.Deck.Hand);
        Assert.Contains("strike", run.Deck.DiscardPile);
    }

    [Fact]
    public void ConsumeCardsFromHand_Exhaust_RemovesFromHandAndAddsToExhaust()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var result = manager.ConsumeCardsFromHand(run.RunId, new[] { "defend" }, CardConsumeDestination.Exhaust);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.DoesNotContain("defend", run.Deck.Hand);
        Assert.Contains("defend", run.Deck.ExhaustPile);
    }

    [Fact]
    public void ConsumeCardsFromHand_None_KeepsCardInHand()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var result = manager.ConsumeCardsFromHand(run.RunId, new[] { "strike" }, CardConsumeDestination.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Contains("strike", run.Deck.Hand);
        Assert.DoesNotContain("strike", run.Deck.DiscardPile);
        Assert.DoesNotContain("strike", run.Deck.ExhaustPile);
    }

    [Fact]
    public void ConsumeCardsFromHand_MissingCard_Fails()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var result = manager.ConsumeCardsFromHand(run.RunId, new[] { "missing" }, CardConsumeDestination.Discard);

        Assert.True(result.IsFailure);
        Assert.Contains("Card not found in hand", result.Error);
    }

    [Fact]
    public void ConsumeCardsFromHand_DuplicateCards_RemovesSingleOccurrence()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;
        run.Deck.Hand.Add("strike");

        var result = manager.ConsumeCardsFromHand(run.RunId, new[] { "strike" }, CardConsumeDestination.Discard);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Single(run.Deck.Hand, card => card == "strike");
        Assert.Single(run.Deck.DiscardPile, card => card == "strike");
    }

    [Fact]
    public void HasCardInHand_ReturnsWhetherCardExists()
    {
        var manager = CreateManager();
        var run = manager.StartRun("test", "default_run", "hero").Value;

        var present = manager.HasCardInHand(run.RunId, "strike");
        var missing = manager.HasCardInHand(run.RunId, "missing");

        Assert.True(present.IsSuccess, present.IsFailure ? present.Error : null);
        Assert.True(missing.IsSuccess, missing.IsFailure ? missing.Error : null);
        Assert.True(present.Value);
        Assert.False(missing.Value);
    }

    private RunManager CreateManager(IScriptModifierManager? scriptModifierManager = null)
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

        return new RunManager(_configManager.Object, _resourceLoader.Object, scriptModifierManager: scriptModifierManager);
    }

    private RunManager CreateManagerWithContent()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("runs/default_run.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["default_run"] = JsonDocument.Parse(RunJson).RootElement.GetProperty("default_run").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("card-selections/pool_reward.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["pool_reward"] = JsonDocument.Parse(PoolCardSelectionJson).RootElement.GetProperty("pool_reward").Clone()
            });
        _resourceLoader
            .Setup(m => m.LoadResource("cards/card_catalog.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(CardCatalogJson));
        _resourceLoader
            .Setup(m => m.LoadResource("card-pools/basic_rewards.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(CardPoolsJson));
        _resourceLoader
            .Setup(m => m.LoadResource("shops/dynamic_shop.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(new Dictionary<string, JsonElement>
            {
                ["dynamic_shop"] = JsonDocument.Parse(DynamicShopJson).RootElement.GetProperty("dynamic_shop").Clone()
            });

        var catalog = new CardContentCatalog(_configManager.Object, _resourceLoader.Object);
        var resolver = new CardPoolResolver(_configManager.Object, _resourceLoader.Object, catalog);
        return new RunManager(_configManager.Object, _resourceLoader.Object, resolver, catalog);
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
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

    private const string PoolCardSelectionJson = """
    {
      "pool_reward": {
        "selectionId": "pool_reward",
        "pickCount": 1,
        "offerCount": 2,
        "cardPoolId": "basic_rewards",
        "reroll": {
          "freeRerolls": 1,
          "baseGoldCost": 10,
          "goldCostPerReroll": 5
        },
        "decompose": {
          "enabled": true
        }
      }
    }
    """;

    private const string CardCatalogJson = """
    {
      "basic_attack": {
        "cardId": "basic_attack",
        "actionId": "basic_attack",
        "rarity": "Common",
        "baseGoldPrice": 10,
        "decomposePowerPoints": 1,
        "tags": ["attack", "common", "starter"]
      },
      "fireball": {
        "cardId": "fireball",
        "actionId": "fireball",
        "rarity": "Uncommon",
        "baseGoldPrice": 25,
        "decomposePowerPoints": 2,
        "tags": ["attack", "fire", "magic", "uncommon"]
      },
      "heal": {
        "cardId": "heal",
        "actionId": "heal",
        "rarity": "Common",
        "baseGoldPrice": 18,
        "decomposePowerPoints": 1,
        "tags": ["heal", "utility", "common"]
      }
    }
    """;

    private const string CardPoolsJson = """
    {
      "basic_rewards": {
        "poolId": "basic_rewards",
        "excludeTags": ["starter"],
        "rarityWeights": {
          "Common": 70,
          "Uncommon": 25
        }
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

    private const string DynamicShopJson = """
    {
      "dynamic_shop": {
        "shopId": "dynamic_shop",
        "cardPoolId": "basic_rewards",
        "offerCount": 2,
        "pricing": {
          "baseMultiplier": 1.0,
          "rarityMultipliers": {
            "Common": 1.0,
            "Uncommon": 1.25
          },
          "tagMultipliers": {
            "fire": 1.1
          }
        },
        "reroll": {
          "baseGoldCost": 10,
          "goldCostPerReroll": 5
        },
        "items": [
          { "itemId": "buy_fireball", "cardId": "fireball", "goldCost": 25, "powerPointCost": 0 }
        ]
      }
    }
    """;

    private const string PreparationJson = """
    {
      "basic_preparation": {
        "preparationId": "basic_preparation",
        "options": [
          { "optionId": "pack_supplies", "goldCost": 5, "powerPointCost": 0, "addCardsToDiscard": ["heal"] },
          {
            "optionId": "train_spell",
            "goldCost": 0,
            "powerPointCost": 1,
            "addCardsToDiscard": ["fireball"],
            "applyModifiers": [
              { "ownerId": "run", "modifierId": "flat_power_bonus", "stacks": 1, "duration": -1, "sourceId": "train_spell" }
            ]
          },
          {
            "optionId": "double_train",
            "goldCost": 0,
            "powerPointCost": 2,
            "addCardsToDiscard": ["fireball"],
            "applyModifiers": [
              { "ownerId": "run", "modifierId": "flat_power_bonus", "stacks": 1, "duration": -1, "sourceId": "double_train" },
              { "ownerId": "run", "modifierId": "missing_modifier", "stacks": 1, "duration": -1, "sourceId": "double_train" }
            ]
          }
        ]
      }
    }
    """;
}
