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
        Assert.Equal(CardRarity.Uncommon, result.Value.Rarity);
        Assert.Equal(25, result.Value.BasePrices.Single(price => price.ResourceId == "gold").Amount);
        Assert.Equal(2, result.Value.DecomposeRewards.Single(reward => reward.ResourceId == "power_points").Amount);
        Assert.Contains("fire", result.Value.Tags);
    }

    [Fact]
    public void GetCard_UsesCardIdAsTheOnlyBehaviorDefinitionIdentity()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCard("heal", "test");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.Equal("heal", result.Value.CardId);
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
        _resourceLoader
            .Setup(m => m.LoadResource("card-pools/missing.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(new Dictionary<string, JsonElement>());

        var result = resolver.ResolvePool("missing", "test");

        Assert.True(result.IsFailure);
        Assert.Contains("Card pool definition not found", result.Error);
    }

    private CardContentCatalog CreateCatalog()
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("cards/card_catalog.json", It.IsAny<IEnumerable<string>>(), true))
            .Returns(ParseResource(CardCatalogJson));

        return new CardContentCatalog(_configManager.Object, _resourceLoader.Object);
    }

    private CardPoolResolver CreateResolver(ICardContentCatalog catalog)
    {
        _configManager.Setup(m => m.ResolveInheritanceChain("test")).Returns(new[] { "test" });
        _resourceLoader
            .Setup(m => m.LoadResource("card-pools/basic_rewards.json", It.IsAny<IEnumerable<string>>(), true))
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
        "rarity": "Common",
        "basePrices": [{ "resourceId": "gold", "amount": 10 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 1 }],
        "tags": ["attack", "common", "starter"]
      },
      "defend": {
        "cardId": "defend",
        "rarity": "Common",
        "basePrices": [{ "resourceId": "gold", "amount": 10 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 1 }],
        "tags": ["defense", "common", "starter"]
      },
      "fireball": {
        "cardId": "fireball",
        "rarity": "Uncommon",
        "basePrices": [{ "resourceId": "gold", "amount": 25 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 2 }],
        "tags": ["attack", "fire", "magic", "uncommon"]
      },
      "heal": {
        "cardId": "heal",
        "rarity": "Common",
        "basePrices": [{ "resourceId": "gold", "amount": 18 }],
        "decomposeRewards": [{ "resourceId": "power_points", "amount": 1 }],
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
