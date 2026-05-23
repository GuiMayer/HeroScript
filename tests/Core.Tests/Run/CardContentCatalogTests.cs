using Core.Config;
using Core.Run.Content;
using Moq;
using System.Text.Json;
using Xunit;

namespace Core.Tests.Run;

public sealed class CardContentCatalogTests
{
    private readonly Mock<IConfigManager> _configManager = new();
    private readonly Mock<IResourceLoader> _resourceLoader = new();

    [Fact]
    public void GetCard_LoadsCardMetadataFromJson()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCard("fireball", "test");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("fireball", result.Value.CardId);
        Assert.Equal("fireball", result.Value.ActionId);
        Assert.Equal(CardRarity.Uncommon, result.Value.Rarity);
        Assert.Equal(25, result.Value.BaseGoldPrice);
        Assert.Equal(2, result.Value.DecomposePowerPoints);
        Assert.Contains("fire", result.Value.Tags);
    }

    [Fact]
    public void GetCard_DefaultsActionIdToCardId()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCard("heal", "test");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("heal", result.Value.ActionId);
    }

    [Fact]
    public void ResolvePool_FiltersByTagsAndRarityWeights()
    {
        var catalog = CreateCatalog();
        var resolver = CreateResolver(catalog);

        var result = resolver.ResolvePool("basic_rewards", "test");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("basic_rewards", result.Value.PoolId);
        Assert.DoesNotContain(result.Value.Cards, card => card.CardId == "basic_attack");
        Assert.DoesNotContain(result.Value.Cards, card => card.CardId == "defend");
        Assert.Contains(result.Value.Cards, card => card.CardId == "fireball" && card.Rarity == CardRarity.Uncommon);
        Assert.Contains(result.Value.Cards, card => card.CardId == "heal" && card.Rarity == CardRarity.Common);
    }

    [Fact]
    public void ResolvePool_FailsForMissingPool()
    {
        var catalog = CreateCatalog();
        var resolver = CreateResolver(catalog);

        var result = resolver.ResolvePool("missing", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("Card pool definition not found", result.Error);
    }

    private CardContentCatalog CreateCatalog()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("cards/card_catalog.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(CardCatalogJson));

        return new CardContentCatalog(_configManager.Object, _resourceLoader.Object);
    }

    private CardPoolResolver CreateResolver(ICardContentCatalog catalog)
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("card-pools/basic_rewards.json", It.IsAny<IEnumerable<string>>(), false))
            .Returns(ParseResource(CardPoolsJson));

        return new CardPoolResolver(_configManager.Object, _resourceLoader.Object, catalog);
    }

    private static Dictionary<string, JsonElement> ParseResource(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

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
      "defend": {
        "cardId": "defend",
        "actionId": "defend",
        "rarity": "Common",
        "baseGoldPrice": 10,
        "decomposePowerPoints": 1,
        "tags": ["defense", "common", "starter"]
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
        "includeTags": [],
        "excludeTags": ["starter"],
        "rarityWeights": {
          "Common": 70,
          "Uncommon": 25,
          "Rare": 5
        }
      }
    }
    """;
}
