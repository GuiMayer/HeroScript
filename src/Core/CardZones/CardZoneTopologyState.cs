using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Common;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneAddress
{
    public string ZoneId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public string? ScopeId { get; init; }

    [JsonIgnore]
    public string Key => CreateKey(ZoneId, OwnerId, ScopeId);

    public static string CreateKey(string zoneId, string ownerId, string? scopeId = null) =>
        $"{scopeId ?? "$run"}::{ownerId}::{zoneId}";
}

public sealed record CardZoneState
{
    private ImmutableList<Guid> _instanceIds = [];

    public CardZoneAddress Address { get; init; } = new();
    public IReadOnlyList<Guid> InstanceIds
    {
        get => _instanceIds;
        init => _instanceIds = value?.ToImmutableList() ?? [];
    }

    internal ImmutableList<Guid> Items => _instanceIds;
}

/// <summary>
/// Purpose-free card topology. The registry owns instance data and every live
/// instance belongs to exactly one addressed zone.
/// </summary>
public sealed record CardZoneTopologyState
{
    public const int CurrentTopologyVersion = 1;

    private ImmutableDictionary<Guid, CardInstanceState> _instances =
        ImmutableDictionary<Guid, CardInstanceState>.Empty;
    private ImmutableDictionary<string, CardZoneState> _zones =
        ImmutableDictionary<string, CardZoneState>.Empty.WithComparers(StringComparer.Ordinal);

    [JsonRequired]
    public int TopologyVersion { get; init; } = CurrentTopologyVersion;
    public IReadOnlyDictionary<Guid, CardInstanceState> Instances
    {
        get => _instances;
        init => _instances = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<Guid, CardInstanceState>.Empty;
    }
    public IReadOnlyDictionary<string, CardZoneState> Zones
    {
        get => _zones;
        init => _zones = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, CardZoneState>.Empty.WithComparers(StringComparer.Ordinal);
    }

    internal ImmutableDictionary<Guid, CardInstanceState> InstanceItems => _instances;
    internal ImmutableDictionary<string, CardZoneState> ZoneItems => _zones;

    public CardZoneState? GetZone(CardZoneAddress address) =>
        _zones.GetValueOrDefault(address.Key);

    public CardZoneState? GetZone(string zoneId, string ownerId, string? scopeId = null) =>
        _zones.GetValueOrDefault(CardZoneAddress.CreateKey(zoneId, ownerId, scopeId));

    public CardInstanceState? GetCard(Guid instanceId) => _instances.GetValueOrDefault(instanceId);

    public CardZoneState? FindZone(Guid instanceId) =>
        _zones.Values.FirstOrDefault(zone => zone.Items.Contains(instanceId));
}

public static class CardZoneTopologyValidator
{
    public static Result Validate(CardZoneTopologyState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.TopologyVersion != CardZoneTopologyState.CurrentTopologyVersion)
            return Result.Failure($"Unsupported card-zone topology version: {state.TopologyVersion}");
        if (state.ZoneItems.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Value.Address.ZoneId) ||
                string.IsNullOrWhiteSpace(pair.Value.Address.OwnerId) ||
                !string.Equals(pair.Key, pair.Value.Address.Key, StringComparison.Ordinal)))
            return Result.Failure("Card-zone topology contains an invalid zone address");
        if (state.InstanceItems.Any(pair =>
                pair.Key == Guid.Empty ||
                pair.Value.CardInstanceId != pair.Key ||
                string.IsNullOrWhiteSpace(pair.Value.DefinitionId) ||
                string.IsNullOrWhiteSpace(pair.Value.OwnerId)))
            return Result.Failure("Card-zone topology contains an invalid card instance");

        var memberships = state.ZoneItems.Values.SelectMany(zone => zone.Items).ToArray();
        if (memberships.Distinct().Count() != memberships.Length)
            return Result.Failure("A card instance cannot belong to more than one zone");
        if (memberships.Any(id => !state.InstanceItems.ContainsKey(id)))
            return Result.Failure("A card zone contains an unknown card instance");
        if (!memberships.ToHashSet().SetEquals(state.InstanceItems.Keys))
            return Result.Failure("Every card instance must belong to exactly one zone");
        return Result.Success();
    }
}
