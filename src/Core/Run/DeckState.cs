namespace Core.Run;

public sealed record DeckState
{
    public List<string> DrawPile { get; init; } = new();
    public List<string> Hand { get; init; } = new();
    public List<string> DiscardPile { get; init; } = new();
    public List<string> ExhaustPile { get; init; } = new();
}
