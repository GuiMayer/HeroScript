using Core.Run.Content;

namespace Core.Run;

public sealed record CardSelectionDefinition
{
    public string SelectionId { get; init; } = string.Empty;
    public int PickCount { get; init; } = 1;
    public int OfferCount { get; init; } = 3;
    public string? CardPoolId { get; init; }
    public List<string> CardPool { get; init; } = new();
    public RerollRulesDefinition Reroll { get; init; } = new();
    public DecomposeRulesDefinition Decompose { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public sealed record RerollRulesDefinition
{
    public int FreeRerolls { get; init; } = 1;
    public int BaseGoldCost { get; init; } = 10;
    public int GoldCostPerReroll { get; init; } = 5;
}

public sealed record DecomposeRulesDefinition
{
    public bool Enabled { get; init; } = true;
}

public sealed record CardSelectionState
{
    public Guid SelectionInstanceId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public string SelectionId { get; init; } = string.Empty;
    public int PickCount { get; init; }
    public int OfferCount { get; init; } = 3;
    public string? CardPoolId { get; init; }
    public List<CardSelectionOptionState> Options { get; init; } = new();
    public int RerollsUsed { get; set; }
    public int FreeRerollsRemaining { get; set; }
    public int RerollCostGold { get; set; }
    public bool Completed { get; set; }
    public List<string> PickedCardIds { get; init; } = new();
    public List<string> DecomposedCardIds { get; init; } = new();
    public RerollRulesDefinition Reroll { get; init; } = new();
    public DecomposeRulesDefinition Decompose { get; init; } = new();
}

public sealed record CardSelectionOptionState
{
    public string CardId { get; init; } = string.Empty;
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public List<string> Tags { get; init; } = new();
    public int DecomposePowerPoints { get; init; } = 1;
    public bool Decomposed { get; set; }
}
