using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneActivationStartResult(
    DeckState Deck,
    DeterministicContext Context,
    IReadOnlyList<string> DrawnCards);

public sealed record CardZoneActivationEndResult(
    DeckState Deck,
    DeterministicContext Context,
    IReadOnlyList<string> DiscardedInstanceIds,
    IReadOnlyList<string> ExhaustedInstanceIds);

/// <summary>Adapts purpose-free zone flow results to combat lifecycle payloads.</summary>
public static class CardZoneCombatLifecycle
{
    public static Result<CardZoneActivationStartResult> Start(
        ICardZoneFlowExecutor? flows, RunState run, DeckState deck,
        DeterministicContext context, string actorId)
    {
        var previousPlayable = CardZonePlaySource.CardsForActor(run with { Deck = deck }, actorId).ToHashSet();
        var flowed = CardZoneRunFlowDispatcher.Execute(flows, run, deck,
            context, "activation.started", actorId);
        if (flowed.IsFailure) return Result<CardZoneActivationStartResult>.Failure(flowed.Error);
        var next = new DeckState { Topology = flowed.Value.State };
        return Result<CardZoneActivationStartResult>.Success(new CardZoneActivationStartResult(
            next, flowed.Value.Context,
            CardZonePlaySource.CardsForActor(run with { Deck = next }, actorId)
                .Where(id => !previousPlayable.Contains(id))
                .Select(id => next.GetDefinitionId(id)!).ToImmutableArray()));
    }

    public static Result<CardZoneActivationEndResult> End(
        ICardZoneFlowExecutor? flows, RunState run, DeckState deck,
        DeterministicContext context, string actorId)
    {
        var previousDiscard = deck.DiscardPileInstanceIds.ToHashSet();
        var previousExhaust = deck.ExhaustPileInstanceIds.ToHashSet();
        var flowed = CardZoneRunFlowDispatcher.Execute(flows, run, deck,
            context, "activation.ended", actorId);
        if (flowed.IsFailure) return Result<CardZoneActivationEndResult>.Failure(flowed.Error);
        var next = new DeckState { Topology = flowed.Value.State };
        return Result<CardZoneActivationEndResult>.Success(new CardZoneActivationEndResult(
            next, flowed.Value.Context,
            next.DiscardPileInstanceIds.Where(id => !previousDiscard.Contains(id))
                .Select(id => id.ToString()).ToImmutableArray(),
            next.ExhaustPileInstanceIds.Where(id => !previousExhaust.Contains(id))
                .Select(id => id.ToString()).ToImmutableArray()));
    }
}
