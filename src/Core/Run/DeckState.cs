using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.CardZones;

namespace Core.Run;

/// <summary>Purpose-free persisted card topology owned by a run.</summary>
public sealed record DeckState
{
    public const int CurrentTopologyVersion = 3;
    private CardZoneTopologyState _topology = CardZoneTransitions.CreateEmpty([]).Value;

    [JsonRequired]
    public int TopologyVersion { get; init; } = CurrentTopologyVersion;

    [JsonRequired]
    public CardZoneTopologyState Topology
    {
        get => _topology;
        init => _topology = value ?? throw new ArgumentNullException(nameof(value));
    }

    public CardInstanceState? GetCard(Guid cardInstanceId) => _topology.GetCard(cardInstanceId);
    public string? GetDefinitionId(Guid cardInstanceId) => GetCard(cardInstanceId)?.DefinitionId;
    public IReadOnlyList<Guid> GetZoneInstanceIds(string zoneId, string ownerId, string? scopeId = null) =>
        _topology.GetZone(zoneId, ownerId, scopeId)?.InstanceIds ?? [];
    public IReadOnlyList<string> GetZoneDefinitionIds(string zoneId, string ownerId, string? scopeId = null) =>
        ResolveDefinitionIds(GetZoneInstanceIds(zoneId, ownerId, scopeId));
    public IReadOnlyList<string> ResolveDefinitionIds(IEnumerable<Guid> cardInstanceIds) =>
        cardInstanceIds.Select(id => GetDefinitionId(id) ?? throw new InvalidOperationException(
            $"Card zone contains unknown instance: {id}")).ToImmutableArray();

}
