using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.CardZones;
using Core.Determinism;

namespace Core.Run;

/// <summary>
/// Temporary semantic projection for consumers still awaiting card-zone
/// migration. Topology is the sole instance registry and zone authority.
/// </summary>
public sealed record DeckState
{
    public const int CurrentTopologyVersion = 3;
    private const string LegacyOwnerId = "$run";
    private static readonly string[] LegacyZoneIds = ["draw", "hand", "discard", "exhaust"];

    private CardZoneTopologyState _topology = CardZoneTransitions.CreateEmpty(
        LegacyZoneIds.Select(zoneId => new CardZoneAddress
        {
            ZoneId = zoneId,
            OwnerId = LegacyOwnerId
        })).Value;

    [JsonRequired]
    public int TopologyVersion { get; init; } = CurrentTopologyVersion;

    [JsonRequired]
    public CardZoneTopologyState Topology
    {
        get => _topology;
        init => _topology = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IReadOnlyList<Guid> DrawPileInstanceIds
    {
        get => GetIds("draw");
        init => SetIds("draw", value);
    }
    public IReadOnlyList<Guid> HandInstanceIds
    {
        get => GetIds("hand");
        init => SetIds("hand", value);
    }
    public IReadOnlyList<Guid> DiscardPileInstanceIds
    {
        get => GetIds("discard");
        init => SetIds("discard", value);
    }
    public IReadOnlyList<Guid> ExhaustPileInstanceIds
    {
        get => GetIds("exhaust");
        init => SetIds("exhaust", value);
    }
    public IReadOnlyDictionary<Guid, CardInstanceState> CardInstances
    {
        get => _topology.Instances;
        init => _topology = _topology with
        {
            Instances = value ?? ImmutableDictionary<Guid, CardInstanceState>.Empty
        };
    }
    public IReadOnlyList<Guid> CollectionInstanceIds
    {
        get => _topology.CollectionInstanceIds;
        init
        {
            var instances = _topology.InstanceItems;
            var ordinal = 0UL;
            foreach (var id in value ?? [])
            {
                if (instances.TryGetValue(id, out var instance))
                    instances = instances.SetItem(id, instance with { CreationOrdinal = ordinal });
                ordinal++;
            }
            _topology = _topology with { Instances = instances };
        }
    }

    internal ImmutableList<Guid> DrawPileItems => GetIds("draw").ToImmutableList();
    internal ImmutableList<Guid> HandItems => GetIds("hand").ToImmutableList();
    internal ImmutableList<Guid> DiscardPileItems => GetIds("discard").ToImmutableList();
    internal ImmutableList<Guid> ExhaustPileItems => GetIds("exhaust").ToImmutableList();
    internal ImmutableList<Guid> CollectionItems => _topology.CollectionInstanceIds.ToImmutableList();
    internal ImmutableDictionary<Guid, CardInstanceState> CardInstanceItems => _topology.InstanceItems;

    [JsonIgnore]
    public IReadOnlyList<string> DrawPile
    {
        get => ResolveDefinitionIds(DrawPileInstanceIds);
        init => SetDefinitionProjection("draw", value);
    }
    [JsonIgnore]
    public IReadOnlyList<string> Hand
    {
        get => ResolveDefinitionIds(HandInstanceIds);
        init => SetDefinitionProjection("hand", value);
    }
    [JsonIgnore]
    public IReadOnlyList<string> DiscardPile
    {
        get => ResolveDefinitionIds(DiscardPileInstanceIds);
        init => SetDefinitionProjection("discard", value);
    }
    [JsonIgnore]
    public IReadOnlyList<string> ExhaustPile
    {
        get => ResolveDefinitionIds(ExhaustPileInstanceIds);
        init => SetDefinitionProjection("exhaust", value);
    }

    public CardInstanceState? GetCard(Guid cardInstanceId) => _topology.GetCard(cardInstanceId);
    public string? GetDefinitionId(Guid cardInstanceId) => GetCard(cardInstanceId)?.DefinitionId;
    public IReadOnlyList<string> ResolveDefinitionIds(IEnumerable<Guid> cardInstanceIds) =>
        cardInstanceIds.Select(id => GetDefinitionId(id) ?? throw new InvalidOperationException(
            $"Card zone contains unknown instance: {id}")).ToImmutableArray();

    private IReadOnlyList<Guid> GetIds(string zoneId) =>
        _topology.GetZone(zoneId, LegacyOwnerId)?.InstanceIds ?? [];

    private void SetIds(string zoneId, IEnumerable<Guid>? ids)
    {
        var address = new CardZoneAddress { ZoneId = zoneId, OwnerId = LegacyOwnerId };
        var zone = _topology.GetZone(address) ?? new CardZoneState { Address = address };
        _topology = _topology with
        {
            Zones = _topology.ZoneItems.SetItem(address.Key,
                zone with { InstanceIds = ids?.ToImmutableList() ?? [] })
        };
    }

    private void SetDefinitionProjection(string zoneId, IEnumerable<string>? definitionIds)
    {
        var ids = ImmutableList.CreateBuilder<Guid>();
        var index = 0UL;
        var instances = _topology.InstanceItems;
        foreach (var definitionId in definitionIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(definitionId)) continue;
            var instanceId = DeterministicId.Create(0, index++,
                $"deck-construction:{zoneId}:{definitionId}");
            ids.Add(instanceId);
            instances = instances.SetItem(instanceId, new CardInstanceState
            {
                CardInstanceId = instanceId,
                DefinitionId = definitionId,
                OwnerId = LegacyOwnerId,
                CreationOrdinal = (ulong)instances.Count
            });
        }
        _topology = _topology with { Instances = instances };
        SetIds(zoneId, ids.ToImmutable());
    }
}
