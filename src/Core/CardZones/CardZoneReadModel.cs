using System.Collections.Immutable;
using System.Text.Json;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneSnapshot(
    Guid RunId,
    int Sequence,
    ulong Step,
    string TopologyHash,
    IReadOnlyList<CardZoneView> Zones);

public sealed record CardZoneView(
    string ZoneId,
    string OwnerId,
    string? ScopeId,
    CardZoneOwnerScope OwnerScope,
    CardZoneOrdering Ordering,
    CardZoneVisibilityDefinition Visibility,
    IReadOnlyDictionary<string, JsonElement> Presentation,
    int Count,
    bool ContentsVisible,
    bool OrderVisible,
    IReadOnlyList<CardZoneCardView> Cards);

public sealed record CardZoneCardView(
    Guid CardInstanceId,
    string DefinitionId,
    IReadOnlyList<CardUpgradeState> Upgrades,
    CardInstanceLifetimeDefinition Lifetime);

/// <summary>Read-only client projection; game content defines zone meaning.</summary>
public static class CardZoneReadModel
{
    public static CardZoneSnapshot Project(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var definitions = run.ResolvedMode?.CardZoneSystem?.Zones
            .ToDictionary(zone => zone.ZoneId, StringComparer.Ordinal)
            ?? new Dictionary<string, CardZoneDefinition>(StringComparer.Ordinal);
        var views = run.Deck.Topology.Zones.Values
            .OrderBy(zone => zone.Address.ZoneId, StringComparer.Ordinal)
            .ThenBy(zone => zone.Address.OwnerId, StringComparer.Ordinal)
            .ThenBy(zone => zone.Address.ScopeId, StringComparer.Ordinal)
            .Select(zone =>
            {
                definitions.TryGetValue(zone.Address.ZoneId, out var definition);
                var visibility = definition?.Visibility ?? new CardZoneVisibilityDefinition
                {
                    Contents = CardZoneVisibility.All,
                    Order = CardZoneOrderVisibility.Visible
                };
                var owns = zone.Address.OwnerId == "$run" ||
                           zone.Address.OwnerId == run.PlayerEntityId;
                var contentsVisible = visibility.Contents == CardZoneVisibility.All ||
                    visibility.Contents == CardZoneVisibility.Owner && owns;
                var orderVisible = contentsVisible && visibility.Order == CardZoneOrderVisibility.Visible;
                var ids = orderVisible ? zone.InstanceIds : zone.InstanceIds.OrderBy(id => id).ToArray();
                var cards = contentsVisible
                    ? ids.Select(id => run.Deck.GetCard(id) ?? throw new InvalidOperationException(
                            $"Card zone references an unknown instance: {id}"))
                        .Select(card => new CardZoneCardView(card.CardInstanceId,
                            card.DefinitionId, card.Upgrades, card.Lifetime))
                        .ToImmutableArray()
                    : [];
                return new CardZoneView(zone.Address.ZoneId, zone.Address.OwnerId,
                    zone.Address.ScopeId, definition?.OwnerScope ?? CardZoneOwnerScope.Unspecified,
                    definition?.Ordering ?? CardZoneOrdering.Unspecified,
                    visibility, definition?.Presentation ??
                        new Dictionary<string, JsonElement>(),
                    zone.InstanceIds.Count, contentsVisible, orderVisible, cards);
            })
            .ToImmutableArray();
        return new CardZoneSnapshot(run.RunId, run.Sequence, run.Determinism.Step,
            CanonicalJson.ComputeHash(run.Deck.Topology), views);
    }
}
