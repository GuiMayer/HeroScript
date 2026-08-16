using System.Collections.Immutable;
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

    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
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

public sealed record ShopRerollRules
{
    public int BaseGoldCost { get; init; } = 10;
    public int GoldCostPerReroll { get; init; } = 5;
}

public sealed record ShopState
{
    private ImmutableList<ShopItemState> _items = [];

    public Guid ShopInstanceId { get; init; }
    public Guid RunId { get; init; }
    public string ShopId { get; init; } = string.Empty;
    public string? CardPoolId { get; init; }
    public int OfferCount { get; init; }
    public int RerollsUsed { get; init; }
    public int RerollCostGold { get; init; }
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
    private ImmutableDictionary<string, double> _pricingBreakdown = ImmutableDictionary<string, double>.Empty;

    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
    public int BaseGoldPrice { get; init; }
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
    public IReadOnlyDictionary<string, double> PricingBreakdown
    {
        get => _pricingBreakdown;
        init => _pricingBreakdown = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, double>.Empty;
    }
    public bool Purchased { get; init; }
}
