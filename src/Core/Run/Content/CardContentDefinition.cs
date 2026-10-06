using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Resources;

namespace Core.Run.Content;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardRarity
{
    Common,
    Uncommon,
    Rare,
    Legendary
}

public sealed record CardContentDefinition
{
    private ImmutableList<string> _tags = [];
    private ImmutableArray<CardBundleReference> _componentBundles = [];
    private ImmutableArray<CardTransformationSlotDefinition> _transformationSlots = [];
    private ImmutableArray<CardComponentDefinition> _components = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;
    private ImmutableArray<ResourceAmount> _basePrices = [];
    private ImmutableArray<ResourceAmount> _decomposeRewards = [];

    public string CardId { get; init; } = string.Empty;
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public IReadOnlyList<ResourceAmount> BasePrices
    {
        get => _basePrices;
        init => _basePrices = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ResourceAmount> DecomposeRewards
    {
        get => _decomposeRewards;
        init => _decomposeRewards = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardBundleReference> ComponentBundles
    {
        get => _componentBundles;
        init => _componentBundles = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardTransformationSlotDefinition> TransformationSlots
    {
        get => _transformationSlots;
        init => _transformationSlots = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardComponentDefinition> Components
    {
        get => _components;
        init => _components = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record CardPoolDefinition
{
    private ImmutableList<string> _includeTags = [];
    private ImmutableList<string> _excludeTags = [];
    private ImmutableDictionary<CardRarity, int> _rarityWeights = ImmutableDictionary<CardRarity, int>.Empty;
    private ImmutableList<string> _explicitCardIds = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;

    public string PoolId { get; init; } = string.Empty;
    public IReadOnlyList<string> IncludeTags
    {
        get => _includeTags;
        init => _includeTags = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<string> ExcludeTags
    {
        get => _excludeTags;
        init => _excludeTags = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<CardRarity, int> RarityWeights
    {
        get => _rarityWeights;
        init => _rarityWeights = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<CardRarity, int>.Empty;
    }
    public IReadOnlyList<string> ExplicitCardIds
    {
        get => _explicitCardIds;
        init => _explicitCardIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record CardPoolResult
{
    private ImmutableList<CardContentDefinition> _cards = [];
    private ImmutableDictionary<CardRarity, int> _rarityWeights =
        ImmutableDictionary<CardRarity, int>.Empty;

    public string PoolId { get; init; } = string.Empty;
    public IReadOnlyList<CardContentDefinition> Cards
    {
        get => _cards;
        init => _cards = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<CardRarity, int> RarityWeights
    {
        get => _rarityWeights;
        init => _rarityWeights = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<CardRarity, int>.Empty;
    }
}
