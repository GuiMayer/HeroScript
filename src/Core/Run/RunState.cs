namespace Core.Run;

public sealed record RunState
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public string ConfigName { get; init; } = "default";
    public string PlayerEntityId { get; init; } = "player";
    public int Gold { get; set; }
    public int PowerPoints { get; set; }
    public string? CurrentNodeId { get; set; }
    public DeckState Deck { get; init; } = new();
    public List<CardSelectionState> CardSelections { get; init; } = new();
    public List<ShopState> Shops { get; init; } = new();
    public List<PreparationState> Preparations { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}
