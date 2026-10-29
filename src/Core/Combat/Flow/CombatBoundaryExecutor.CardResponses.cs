using Core.CardZones;
using Core.Combat.Models;
using Core.Common;
using Core.Determinism;
using Core.Run;

namespace Core.Combat.Flow;

public sealed partial class CombatBoundaryExecutor
{
    private Result<CardZoneActivationStartResult> ApplyStartCardZoneLifecycle(
        RunState run,
        DeckState deck,
        string actorId,
        DeterministicContext context, CombatState? combatSnapshot = null, ObservationCursor? observation = null)
    {
        var result = CardZoneCombatLifecycle.Start(_cardZoneFlows, run, deck, context, actorId, combatSnapshot);
        if (result.IsFailure || observation == null || combatSnapshot == null) return result;
        var responses = observation.Cards(result.Value.CardZoneSteps, run with { Deck = deck, Determinism = context },
            run with { Deck = result.Value.Deck, Determinism = result.Value.Context }, combatSnapshot, actorId);
        return Result<CardZoneActivationStartResult>.Success(result.Value with { Deck = responses.Run!.Deck,
            Context = responses.Run.Determinism, Responses = responses });
    }

    private Result<CardZoneActivationEndResult> ApplyEndCardZoneLifecycle(
        RunState run,
        DeckState deck,
        string actorId,
        DeterministicContext context, CombatState? combatSnapshot = null, ObservationCursor? observation = null)
    {
        var result = CardZoneCombatLifecycle.End(_cardZoneFlows, run, deck, context, actorId, combatSnapshot);
        if (result.IsFailure || observation == null || combatSnapshot == null) return result;
        var responses = observation.Cards(result.Value.CardZoneSteps, run with { Deck = deck, Determinism = context },
            run with { Deck = result.Value.Deck, Determinism = result.Value.Context }, combatSnapshot, actorId);
        return Result<CardZoneActivationEndResult>.Success(result.Value with { Deck = responses.Run!.Deck,
            Context = responses.Run.Determinism, Responses = responses });
    }

}
