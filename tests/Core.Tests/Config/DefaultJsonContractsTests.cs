using System.Text.Json;
using Xunit;

namespace Core.Tests.Config;

public class DefaultJsonContractsTests
{
    private static readonly string ResourcesRoot = Path.Combine(
        FindProjectRoot(),
        "data",
        "configs",
        "default",
        "Resources");

    [Fact]
    public void DefaultResources_AllJsonFiles_ParseAsObjects()
    {
        var files = Directory.GetFiles(ResourcesRoot, "*.json", SearchOption.AllDirectories);

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }

    [Fact]
    public void CardCatalog_AllCardsOwnStableComponentContainers()
    {
        var cards = LoadResource("cards", "card_catalog.json");

        Assert.NotEmpty(cards);
        foreach (var (cardId, card) in cards)
        {
            Assert.False(card.TryGetProperty("actionId", out _));
            var components = card.GetProperty("components").EnumerateArray().ToArray();
            Assert.NotEmpty(components);
            var componentIds = components
                .Select(component => RequiredString(component, "componentId", cardId))
                .ToArray();
            Assert.Equal(componentIds.Length, componentIds.Distinct(StringComparer.Ordinal).Count());
            Assert.All(components, component => RequiredString(component, "type", cardId));
        }
    }

    [Fact]
    public void CardPools_ExplicitCardsReferenceCatalogCards()
    {
        var cards = LoadResource("cards", "card_catalog.json").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pools = LoadResource("card-pools", "basic_rewards.json");

        Assert.NotEmpty(pools);
        foreach (var (poolId, pool) in pools)
        {
            if (!pool.TryGetProperty("explicitCardIds", out var explicitCardIds))
                continue;

            foreach (var cardId in explicitCardIds.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                Assert.Contains(cardId!, cards);
        }
    }

    [Fact]
    public void RunDefinition_ReferencesExistingDeckCards()
    {
        var cards = LoadResource("cards", "card_catalog.json").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runs = LoadResource("runs", "default_run.json");

        Assert.NotEmpty(runs);
        foreach (var (runId, run) in runs)
        {
            foreach (var cardId in run.GetProperty("startingDeck").EnumerateArray().Select(x => x.GetString()))
                Assert.Contains(cardId!, cards);
        }
    }

    [Fact]
    public void RewardShopPreparation_ReferenceExistingPoolsCardsAndModifiers()
    {
        var cards = LoadResource("cards", "card_catalog.json").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pools = LoadResource("card-pools", "basic_rewards.json").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var modifiers = LoadResource("modifiers", "script_modifiers.json").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selections = LoadResource("card-selections", "basic_reward.json");
        foreach (var (selectionId, selection) in selections)
        {
            Assert.Contains(RequiredString(selection, "cardPoolId", selectionId), pools);
            Assert.True(selection.GetProperty("offerCount").GetInt32() > 0);
        }

        var shops = LoadResource("shops", "basic_shop.json");
        foreach (var (shopId, shop) in shops)
        {
            Assert.Contains(RequiredString(shop, "cardPoolId", shopId), pools);
            foreach (var item in shop.GetProperty("items").EnumerateArray())
                Assert.Contains(RequiredString(item, "cardId", shopId), cards);
        }

        var preparations = LoadResource("preparations", "basic_preparation.json");
        foreach (var (preparationId, preparation) in preparations)
        {
            foreach (var option in preparation.GetProperty("options").EnumerateArray())
            {
                if (option.TryGetProperty("addCardsToDiscard", out var addCards))
                {
                    foreach (var cardId in addCards.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                        Assert.Contains(cardId!, cards);
                }

                if (option.TryGetProperty("applyModifiers", out var applyModifiers))
                {
                    foreach (var modifier in applyModifiers.EnumerateArray())
                        Assert.Contains(RequiredString(modifier, "modifierId", preparationId), modifiers);
                }
            }
        }
    }

    [Fact]
    public void Entities_ReferenceExistingActionsAndResources()
    {
        var actions = Directory.GetFiles(Path.Combine(ResourcesRoot, "actions"), "*.json")
            .SelectMany(file => LoadResource("actions", Path.GetFileName(file)).Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resources = Directory.GetFiles(Path.Combine(ResourcesRoot, "resources"), "*.json")
            .Select(file => LoadSingleResourceId("resources", Path.GetFileName(file), "resourceId"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entityFiles = Directory.GetFiles(Path.Combine(ResourcesRoot, "entities"), "*.json");
        Assert.NotEmpty(entityFiles);

        foreach (var file in entityFiles)
        {
            foreach (var (_, entity) in LoadResource("entities", Path.GetFileName(file)))
            {
                foreach (var component in entity.GetProperty("components").EnumerateArray())
                {
                    var type = component.GetProperty("type").GetString();
                    if (type == "resources")
                    {
                        foreach (var resource in component.GetProperty("pools").EnumerateObject())
                            Assert.Contains(resource.Name, resources);
                    }
                    if (type == "abilities")
                    {
                        foreach (var actionId in component.GetProperty("abilityIds").EnumerateArray()
                                     .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)))
                            Assert.Contains(actionId!, actions);
                    }
                }
            }
        }
    }

    private static Dictionary<string, JsonElement> LoadResource(string directory, string fileName)
    {
        var path = Path.Combine(ResourcesRoot, directory, fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static string LoadSingleResourceId(string directory, string fileName, string idPropertyName)
    {
        var path = Path.Combine(ResourcesRoot, directory, fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var definitions = document.RootElement.EnumerateObject().ToArray();
        var definition = Assert.Single(definitions);
        Assert.Equal(Path.GetFileNameWithoutExtension(fileName), definition.Name);
        return RequiredString(definition.Value, idPropertyName, fileName);
    }

    private static string RequiredString(JsonElement element, string propertyName, string ownerId)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"{ownerId} must define {propertyName}");
        var value = property.GetString();
        Assert.False(string.IsNullOrWhiteSpace(value), $"{ownerId}.{propertyName} cannot be empty");
        return value!;
    }

    private static string FindProjectRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, "data", "configs", "default", "Resources")))
                return current;

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not find HeroScript project root.");
    }
}
