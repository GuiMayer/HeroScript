using System.Collections.Immutable;

namespace Core.Run;

public sealed record DeckState
{
    private ImmutableList<string> _drawPile = [];
    private ImmutableList<string> _hand = [];
    private ImmutableList<string> _discardPile = [];
    private ImmutableList<string> _exhaustPile = [];

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

    internal ImmutableList<string> DrawPileItems => _drawPile;
    internal ImmutableList<string> HandItems => _hand;
    internal ImmutableList<string> DiscardPileItems => _discardPile;
    internal ImmutableList<string> ExhaustPileItems => _exhaustPile;
}
