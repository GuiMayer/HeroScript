using System.Text.Json.Serialization;

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
    public string CardId { get; init; } = string.Empty;
    public string ActionId { get; init; } = string.Empty;
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public int BaseGoldPrice { get; init; }
    public int DecomposePowerPoints { get; init; } = 1;
    public List<string> Tags { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record CardPoolDefinition
{
    public string PoolId { get; init; } = string.Empty;
    public List<string> IncludeTags { get; init; } = new();
    public List<string> ExcludeTags { get; init; } = new();
    public Dictionary<CardRarity, int> RarityWeights { get; init; } = new();
    public List<string> ExplicitCardIds { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record CardPoolResult
{
    public string PoolId { get; init; } = string.Empty;
    public IReadOnlyList<CardContentDefinition> Cards { get; init; } = Array.Empty<CardContentDefinition>();
}
