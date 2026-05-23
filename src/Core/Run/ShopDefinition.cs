using Core.Run.Content;

namespace Core.Run;

public sealed record ShopDefinition
{
    public string ShopId { get; init; } = string.Empty;
    public string? CardPoolId { get; init; }
    public int OfferCount { get; init; } = 5;
    public List<ShopItemDefinition> Items { get; init; } = new();
    public ShopPricingRules Pricing { get; init; } = new();
    public ShopRerollRules Reroll { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record ShopItemDefinition
{
    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record ShopPricingRules
{
    public double BaseMultiplier { get; init; } = 1.0;
    public Dictionary<CardRarity, double> RarityMultipliers { get; init; } = new();
    public Dictionary<string, double> TagMultipliers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ShopRerollRules
{
    public int BaseGoldCost { get; init; } = 10;
    public int GoldCostPerReroll { get; init; } = 5;
}

public sealed record ShopState
{
    public Guid ShopInstanceId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public string ShopId { get; init; } = string.Empty;
    public string? CardPoolId { get; init; }
    public int OfferCount { get; init; }
    public int RerollsUsed { get; set; }
    public int RerollCostGold { get; set; }
    public ShopPricingRules Pricing { get; init; } = new();
    public ShopRerollRules Reroll { get; init; } = new();
    public List<ShopItemState> Items { get; init; } = new();
}

public sealed record ShopItemState
{
    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public List<string> Tags { get; init; } = new();
    public int BaseGoldPrice { get; init; }
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
    public Dictionary<string, double> PricingBreakdown { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Purchased { get; set; }
}
