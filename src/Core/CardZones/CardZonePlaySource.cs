using System.Collections.Immutable;
using Core.Run;

namespace Core.CardZones;

/// <summary>
/// A game mode marks play sources explicitly. Visual presentation slots do not
/// grant permission to play; ownership is checked against the acting actor.
/// </summary>
public static class CardZonePlaySource
{
    public static IReadOnlyList<Guid> CardsForActor(RunState run, string actorId)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrWhiteSpace(actorId)) return [];
        var graph = run.ResolvedMode?.CardZoneSystem;
        if (graph == null)
            return [];

        var definitions = graph.Zones.Where(zone => zone.AllowsCardPlay)
            .ToDictionary(zone => zone.ZoneId, StringComparer.Ordinal);
        return run.Deck.Topology.Zones.Values
            .Where(zone => definitions.TryGetValue(zone.Address.ZoneId, out var definition) &&
                CanPlayFrom(run, actorId, zone.Address, definition))
            .OrderBy(zone => zone.Address.ZoneId, StringComparer.Ordinal)
            .ThenBy(zone => zone.Address.OwnerId, StringComparer.Ordinal)
            .ThenBy(zone => zone.Address.ScopeId, StringComparer.Ordinal)
            .SelectMany(zone => zone.InstanceIds)
            .ToImmutableArray();
    }

    public static bool Contains(RunState run, string actorId, Guid instanceId) =>
        CardsForActor(run, actorId).Contains(instanceId);

    private static bool CanPlayFrom(
        RunState run, string actorId, CardZoneAddress address, CardZoneDefinition definition) =>
        definition.OwnerScope switch
        {
            CardZoneOwnerScope.Actor => string.Equals(address.OwnerId, actorId, StringComparison.Ordinal),
            CardZoneOwnerScope.RunOwner =>
                string.Equals(actorId, run.PlayerEntityId, StringComparison.Ordinal) &&
                string.Equals(address.OwnerId, "$run", StringComparison.Ordinal),
            CardZoneOwnerScope.Global => string.Equals(address.OwnerId, "$global", StringComparison.Ordinal),
            _ => false
        };
}
