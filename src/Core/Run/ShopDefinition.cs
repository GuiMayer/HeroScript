namespace Core.Run;

public sealed record ShopDefinition
{
    public string ShopId { get; init; } = string.Empty;
    public List<ShopItemDefinition> Items { get; init; } = new();
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

public sealed record ShopState
{
    public Guid ShopInstanceId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public string ShopId { get; init; } = string.Empty;
    public List<ShopItemState> Items { get; init; } = new();
}

public sealed record ShopItemState
{
    public string ItemId { get; init; } = string.Empty;
    public string? CardId { get; init; }
    public int GoldCost { get; init; }
    public int PowerPointCost { get; init; }
    public bool Purchased { get; set; }
}
