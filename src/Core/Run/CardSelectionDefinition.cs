using System.Collections.Immutable;
using Core.Run.Content;

namespace Core.Run;

public sealed record CardSelectionDefinition
{
    private ImmutableList<string> _cardPool = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;

    public string SelectionId { get; init; } = string.Empty;
    public int PickCount { get; init; } = 1;
    public int OfferCount { get; init; } = 3;
    public string? CardPoolId { get; init; }
    public IReadOnlyList<string> CardPool
    {
        get => _cardPool;
        init => _cardPool = value?.ToImmutableList() ?? [];
    }
    public RerollRulesDefinition Reroll { get; init; } = new();
    public DecomposeRulesDefinition Decompose { get; init; } = new();
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
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
    private ImmutableList<CardSelectionOptionState> _options = [];
    private ImmutableList<string> _pickedCardIds = [];
    private ImmutableList<string> _decomposedCardIds = [];

    public Guid SelectionInstanceId { get; init; }
    public Guid RunId { get; init; }
    public string SelectionId { get; init; } = string.Empty;
    public int PickCount { get; init; }
    public int OfferCount { get; init; } = 3;
    public string? CardPoolId { get; init; }
    public string OfferFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<CardSelectionOptionState> Options
    {
        get => _options;
        init => _options = value?.ToImmutableList() ?? [];
    }
    public int RerollsUsed { get; init; }
    public int FreeRerollsRemaining { get; init; }
    public int RerollCostGold { get; init; }
    public bool Completed { get; init; }
    public IReadOnlyList<string> PickedCardIds
    {
        get => _pickedCardIds;
        init => _pickedCardIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<string> DecomposedCardIds
    {
        get => _decomposedCardIds;
        init => _decomposedCardIds = value?.ToImmutableList() ?? [];
    }
    public RerollRulesDefinition Reroll { get; init; } = new();
    public DecomposeRulesDefinition Decompose { get; init; } = new();
}

public sealed record CardSelectionOptionState
{
    private ImmutableList<string> _tags = [];

    public string CardId { get; init; } = string.Empty;
    public CardRarity Rarity { get; init; } = CardRarity.Common;
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableList() ?? [];
    }
    public int DecomposePowerPoints { get; init; } = 1;
    public bool Decomposed { get; init; }
}
