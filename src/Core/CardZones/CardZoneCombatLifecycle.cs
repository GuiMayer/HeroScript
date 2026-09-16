using System.Collections.Immutable;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneActivationStartResult(
    DeckState Deck,
    DeterministicContext Context,
    IReadOnlyList<string> NewlyPlayableDefinitionIds)
{
    public ImmutableArray<CardZoneFlowStepRecord> CardZoneSteps { get; init; } = [];
}

public sealed record CardZoneActivationEndResult(
    DeckState Deck,
    DeterministicContext Context,
    IReadOnlyList<string> AffectedInstanceIds)
{
    public ImmutableArray<CardZoneFlowStepRecord> CardZoneSteps { get; init; } = [];
}

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
                .Select(id => next.GetDefinitionId(id)!).ToImmutableArray())
        {
            CardZoneSteps = flowed.Value.Steps
        });
    }

    public static Result<CardZoneActivationEndResult> End(
        ICardZoneFlowExecutor? flows, RunState run, DeckState deck,
        DeterministicContext context, string actorId)
    {
        var flowed = CardZoneRunFlowDispatcher.Execute(flows, run, deck,
            context, "activation.ended", actorId);
        if (flowed.IsFailure) return Result<CardZoneActivationEndResult>.Failure(flowed.Error);
        var next = new DeckState { Topology = flowed.Value.State };
        return Result<CardZoneActivationEndResult>.Success(new CardZoneActivationEndResult(
            next, flowed.Value.Context,
            flowed.Value.Steps.SelectMany(step => step.InstanceIds
                    .Concat(step.CreatedInstanceIds).Concat(step.DestroyedInstanceIds))
                .Distinct()
                .Select(id => id.ToString()).ToImmutableArray())
        {
            CardZoneSteps = flowed.Value.Steps
        });
    }
}
