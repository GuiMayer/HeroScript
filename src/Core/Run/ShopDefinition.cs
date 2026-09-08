using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Resources;
using Core.Run.Content;

namespace Core.Run;

public sealed record ShopDefinition
{
    private ImmutableList<ShopItemDefinition> _items = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;

    public string ShopId { get; init; } = string.Empty;
    public string? CardPoolId { get; init; }
    public int OfferCount { get; init; } = 5;
    public IReadOnlyList<ShopItemDefinition> Items
    {
        get => _items;
        init => _items = value?.ToImmutableList() ?? [];
    }
    public ShopPricingRules Pricing { get; init; } = new();
    public ShopRerollRules Reroll { get; init; } = new();
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record ShopItemDefinition
{
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;
    private ImmutableArray<ResourceAmount> _costs = [];

    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public IReadOnlyList<ResourceAmount> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record ShopPricingRules
{
    private ImmutableDictionary<CardRarity, double> _rarityMultipliers = ImmutableDictionary<CardRarity, double>.Empty;
    private ImmutableDictionary<string, double> _tagMultipliers = ImmutableDictionary<string, double>.Empty;

    public double BaseMultiplier { get; init; } = 1.0;
    public ResourcePriceRounding Rounding { get; init; } = ResourcePriceRounding.Ceiling;
    public IReadOnlyDictionary<CardRarity, double> RarityMultipliers
    {
        get => _rarityMultipliers;
        init => _rarityMultipliers = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<CardRarity, double>.Empty;
    }
    public IReadOnlyDictionary<string, double> TagMultipliers
    {
        get => _tagMultipliers;
        init => _tagMultipliers = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, double>.Empty;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ResourcePriceRounding
{
    None,
    Floor,
    Ceiling,
    Nearest
}

public sealed record ShopRerollRules
{
    private ImmutableArray<ResourceAmount> _baseCosts = [];
    private ImmutableArray<ResourceAmount> _costsPerReroll = [];

    public IReadOnlyList<ResourceAmount> BaseCosts
    {
        get => _baseCosts;
        init => _baseCosts = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ResourceAmount> CostsPerReroll
    {
        get => _costsPerReroll;
        init => _costsPerReroll = value?.ToImmutableArray() ?? [];
    }
}

public sealed record ShopState
{
    private ImmutableList<ShopItemState> _items = [];

    public Guid ShopInstanceId { get; init; }
    public Guid RunId { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public string ShopId { get; init; } = string.Empty;
    public string? CardPoolId { get; init; }
    public string OfferFingerprint { get; init; } = string.Empty;
    public int OfferCount { get; init; }
    public int RerollsUsed { get; init; }
    public ImmutableArray<ResourceAmount> RerollCosts { get; init; } = [];
    public ShopPricingRules Pricing { get; init; } = new();
    public ShopRerollRules Reroll { get; init; } = new();
    public IReadOnlyList<ShopItemState> Items
    {
        get => _items;
        init => _items = value?.ToImmutableList() ?? [];
    }
}

public sealed record ShopItemState
{
    private ImmutableList<string> _tags = [];
    private ImmutableArray<ResourceAmount> _baseCosts = [];
    private ImmutableArray<ResourceAmount> _costs = [];
    private ImmutableDictionary<string, IReadOnlyDictionary<string, double>> _pricingBreakdowns =
        ImmutableDictionary<string, IReadOnlyDictionary<string, double>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<ResourceAmount> BaseCosts
    {
        get => _baseCosts;
        init => _baseCosts = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ResourceAmount> Costs
    {
        get => _costs;
        init => _costs = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> PricingBreakdowns
    {
        get => _pricingBreakdowns;
        init => _pricingBreakdowns = value?.ToImmutableDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, double>)pair.Value.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, IReadOnlyDictionary<string, double>>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
    public bool Purchased { get; init; }
}
