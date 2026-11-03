using System.Collections.Immutable;

namespace Core.Run.DeckLoadouts;

/// <summary>
/// Derived selectors for the campaign player's build. Zone ownership, movement and
/// lifecycle remain authored by the zone graph; these sets never move a card.
/// </summary>
public static class DeckLoadoutZoneSets
{
    public static ImmutableDictionary<string, ImmutableArray<Guid>> Resolve(RunState run)
    {
        var sets = ImmutableDictionary.CreateBuilder<string, ImmutableArray<Guid>>(StringComparer.Ordinal);
        var captured = run.GetActiveEncounter()?.DeckComposition;
        var groups = captured?.Groups;
        if (groups == null && run.DeckLoadout?.Actors.TryGetValue(run.PlayerEntityId, out var actor) == true)
            groups = actor.Groups;
        if (groups == null) return sets.ToImmutable();
        var active = captured?.ActiveCardIds ?? groups.Values.SelectMany(ids => ids).ToImmutableArray();
        var membership = active.ToImmutableHashSet();
        sets["deck.active"] = active;
        sets["deck.reserve"] = DeckLoadoutPlanner.Owned(run, run.PlayerEntityId)
            .Where(card => !membership.Contains(card.CardInstanceId)).Select(card => card.CardInstanceId).ToImmutableArray();
        foreach (var group in groups) sets[$"deck.group.{group.Key}"] = group.Value;
        return sets.ToImmutable();
    }
}
