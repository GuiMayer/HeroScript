using System.Collections.Immutable;

namespace Core.Run;

public sealed record DeckState
{
    private ImmutableList<string> _drawPile = [];
    private ImmutableList<string> _hand = [];
    private ImmutableList<string> _discardPile = [];
    private ImmutableList<string> _exhaustPile = [];
    private ImmutableList<Guid> _drawPileInstanceIds = [];
    private ImmutableList<Guid> _handInstanceIds = [];
    private ImmutableList<Guid> _discardPileInstanceIds = [];
    private ImmutableList<Guid> _exhaustPileInstanceIds = [];
    private ImmutableDictionary<Guid, CardInstanceState> _cardInstances =
        ImmutableDictionary<Guid, CardInstanceState>.Empty;

    /// <summary>
    /// Enables the instance topology for new runs while preserving the legacy
    /// definition-only representation when old snapshots are deserialized.
    /// </summary>
    public bool InstanceTrackingEnabled { get; init; }

    public IReadOnlyList<string> DrawPile
    {
        get => _drawPile;
        init => _drawPile = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<string> Hand
    {
        get => _hand;
        init => _hand = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<string> DiscardPile
    {
        get => _discardPile;
        init => _discardPile = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<string> ExhaustPile
    {
        get => _exhaustPile;
        init => _exhaustPile = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<Guid> DrawPileInstanceIds
    {
        get => _drawPileInstanceIds;
        init => _drawPileInstanceIds = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<Guid> HandInstanceIds
    {
        get => _handInstanceIds;
        init => _handInstanceIds = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<Guid> DiscardPileInstanceIds
    {
        get => _discardPileInstanceIds;
        init => _discardPileInstanceIds = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyList<Guid> ExhaustPileInstanceIds
    {
        get => _exhaustPileInstanceIds;
        init => _exhaustPileInstanceIds = value?.ToImmutableList() ?? [];
    }

    public IReadOnlyDictionary<Guid, CardInstanceState> CardInstances
    {
        get => _cardInstances;
        init => _cardInstances = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<Guid, CardInstanceState>.Empty;
    }

    internal ImmutableList<string> DrawPileItems => _drawPile;
    internal ImmutableList<string> HandItems => _hand;
    internal ImmutableList<string> DiscardPileItems => _discardPile;
    internal ImmutableList<string> ExhaustPileItems => _exhaustPile;
    internal ImmutableList<Guid> DrawPileInstanceIdItems => _drawPileInstanceIds;
    internal ImmutableList<Guid> HandInstanceIdItems => _handInstanceIds;
    internal ImmutableList<Guid> DiscardPileInstanceIdItems => _discardPileInstanceIds;
    internal ImmutableList<Guid> ExhaustPileInstanceIdItems => _exhaustPileInstanceIds;
    internal ImmutableDictionary<Guid, CardInstanceState> CardInstanceItems => _cardInstances;
}
