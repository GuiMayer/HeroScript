using System.Collections.Immutable;
using Core.Common;
using Core.Run;

namespace Core.CardZones;

/// <summary>Fulfills a validated gameplay award through the pinned zone graph.</summary>
public static class CardZoneGrantTransitions
{
    public static Result<DeckTransition> Grant(
        RunState run,
        IReadOnlyList<string> cardDefinitionIds,
        ICardZoneFlowExecutor? flows)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(cardDefinitionIds);
        if (cardDefinitionIds.Any(string.IsNullOrWhiteSpace))
            return Result<DeckTransition>.Failure("Granted card definition ids must be nonempty");
        if (cardDefinitionIds.Count == 0)
            return Result<DeckTransition>.Success(new DeckTransition(run.Deck, run.Determinism, []));

        var result = CardZoneRunFlowDispatcher.Grant(flows, run, cardDefinitionIds);
        if (result.IsFailure) return Result<DeckTransition>.Failure(result.Error);
        var created = result.Value.Steps.SelectMany(step => step.CreatedInstanceIds)
            .Distinct().Select(id => result.Value.State.GetCard(id)?.DefinitionId)
            .ToArray();
        if (created.Any(id => id == null) ||
            !created.OrderBy(id => id, StringComparer.Ordinal)
                .SequenceEqual(cardDefinitionIds.OrderBy(id => id, StringComparer.Ordinal), StringComparer.Ordinal))
            return Result<DeckTransition>.Failure("Gameplay grant flow did not create the exact awarded cards");

        return Result<DeckTransition>.Success(new DeckTransition(
            new DeckState { Topology = result.Value.State },
            result.Value.Context,
            cardDefinitionIds.ToImmutableArray()));
    }
}
