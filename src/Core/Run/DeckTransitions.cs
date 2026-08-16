using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;

namespace Core.Run;

public sealed record DeckTransition(
    DeckState State,
    DeterministicContext Context,
    ImmutableArray<string> Cards);

/// <summary>
/// Pure deck state machine. Input instances are never modified.
/// </summary>
public static class DeckTransitions
{
    public static Result<DeckTransition> Draw(
        DeckState state,
        int count,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (count < 0)
            return Result<DeckTransition>.Failure("Draw count cannot be negative");

        var current = state;
        var currentContext = context;
        var drawn = ImmutableArray.CreateBuilder<string>(count);

        for (var index = 0; index < count; index++)
        {
            if (current.DrawPileItems.IsEmpty)
            {
                var shuffled = ShuffleDiscardIntoDrawPile(current, currentContext);
                current = shuffled.State;
                currentContext = shuffled.Context;
            }

            if (current.DrawPileItems.IsEmpty)
                break;

            var cardId = current.DrawPileItems[0];
            current = current with
            {
                DrawPile = current.DrawPileItems.RemoveAt(0),
                Hand = current.HandItems.Add(cardId)
            };
            drawn.Add(cardId);
        }

        return Result<DeckTransition>.Success(
            new DeckTransition(current, currentContext, drawn.MoveToImmutable()));
    }

    public static Result<DeckTransition> AddToHand(
        DeckState state,
        IReadOnlyList<string> cardIds,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(context);

        var cards = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToImmutableArray();
        return Result<DeckTransition>.Success(
            new DeckTransition(state with { Hand = state.HandItems.AddRange(cards) }, context, cards));
    }

    public static Result<DeckTransition> AddToDiscard(
        DeckState state,
        IReadOnlyList<string> cardIds,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(context);

        var cards = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToImmutableArray();
        return Result<DeckTransition>.Success(
            new DeckTransition(state with { DiscardPile = state.DiscardPileItems.AddRange(cards) }, context, cards));
    }

    public static Result<DeckTransition> MoveFromHand(
        DeckState state,
        IReadOnlyList<string> cardIds,
        CardConsumeDestination destination,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cardIds);
        ArgumentNullException.ThrowIfNull(context);

        var cards = cardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToImmutableArray();
        if (cards.IsEmpty)
            return Result<DeckTransition>.Failure("At least one card id is required");

        if (destination is not (CardConsumeDestination.None or CardConsumeDestination.Discard or CardConsumeDestination.Exhaust))
            return Result<DeckTransition>.Failure($"Unsupported card consume destination: {destination}");

        var remainingHand = state.HandItems.ToBuilder();
        foreach (var cardId in cards)
        {
            if (!remainingHand.Remove(cardId))
                return Result<DeckTransition>.Failure($"Card not found in hand: {cardId}");
        }

        if (destination == CardConsumeDestination.None)
            return Result<DeckTransition>.Success(new DeckTransition(state, context, cards));

        var next = state with { Hand = remainingHand.ToImmutable() };
        next = destination == CardConsumeDestination.Discard
            ? next with { DiscardPile = next.DiscardPileItems.AddRange(cards) }
            : next with { ExhaustPile = next.ExhaustPileItems.AddRange(cards) };

        return Result<DeckTransition>.Success(new DeckTransition(next, context, cards));
    }

    public static DeckTransition ShuffleDiscardIntoDrawPile(
        DeckState state,
        DeterministicContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);

        if (state.DiscardPileItems.IsEmpty)
            return new DeckTransition(state, context, []);

        var shuffled = state.DiscardPileItems.ToBuilder();
        var currentContext = context;
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var draw = currentContext.DrawInt32(index + 1);
            currentContext = draw.Context;
            (shuffled[index], shuffled[draw.Value]) = (shuffled[draw.Value], shuffled[index]);
        }

        var cards = shuffled.ToImmutable();
        var next = state with
        {
            DrawPile = state.DrawPileItems.AddRange(cards),
            DiscardPile = []
        };
        return new DeckTransition(next, currentContext, cards.ToImmutableArray());
    }
}
