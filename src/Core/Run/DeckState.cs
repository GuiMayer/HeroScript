using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Core.Determinism;

namespace Core.Run;

/// <summary>
/// Immutable card topology. The instance registry is the permanent collection
/// owned by the run; combat zones contain instance IDs only.
/// </summary>
public sealed record DeckState
{
    public const int CurrentTopologyVersion = 1;

    private ImmutableList<Guid> _drawPile = [];
    private ImmutableList<Guid> _hand = [];
    private ImmutableList<Guid> _discardPile = [];
    private ImmutableList<Guid> _exhaustPile = [];
    private ImmutableDictionary<Guid, CardInstanceState> _cardInstances =
        ImmutableDictionary<Guid, CardInstanceState>.Empty;

    [JsonRequired]
    public int TopologyVersion { get; init; } = CurrentTopologyVersion;

    public IReadOnlyList<Guid> DrawPileInstanceIds
    {
        get => _drawPile;
        init => _drawPile = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<Guid> HandInstanceIds
    {
        get => _hand;
        init => _hand = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<Guid> DiscardPileInstanceIds
    {
        get => _discardPile;
        init => _discardPile = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<Guid> ExhaustPileInstanceIds
    {
        get => _exhaustPile;
        init => _exhaustPile = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<Guid, CardInstanceState> CardInstances
    {
        get => _cardInstances;
        init => _cardInstances = value?.ToImmutableDictionary()
            ?? ImmutableDictionary<Guid, CardInstanceState>.Empty;
    }

    internal ImmutableList<Guid> DrawPileItems => _drawPile;
    internal ImmutableList<Guid> HandItems => _hand;
    internal ImmutableList<Guid> DiscardPileItems => _discardPile;
    internal ImmutableList<Guid> ExhaustPileItems => _exhaustPile;
    internal ImmutableDictionary<Guid, CardInstanceState> CardInstanceItems => _cardInstances;

    /// <summary>
    /// Definition-oriented construction/display adapter. It is never serialized;
    /// persisted topology is represented exclusively by instance IDs.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> DrawPile
    {
        get => ResolveDefinitionIds(_drawPile);
        init => SetDefinitionProjection(ref _drawPile, value, "draw");
    }
    [JsonIgnore]
    public IReadOnlyList<string> Hand
    {
        get => ResolveDefinitionIds(_hand);
        init => SetDefinitionProjection(ref _hand, value, "hand");
    }
    [JsonIgnore]
    public IReadOnlyList<string> DiscardPile
    {
        get => ResolveDefinitionIds(_discardPile);
        init => SetDefinitionProjection(ref _discardPile, value, "discard");
    }
    [JsonIgnore]
    public IReadOnlyList<string> ExhaustPile
    {
        get => ResolveDefinitionIds(_exhaustPile);
        init => SetDefinitionProjection(ref _exhaustPile, value, "exhaust");
    }

    public CardInstanceState? GetCard(Guid cardInstanceId) =>
        _cardInstances.GetValueOrDefault(cardInstanceId);

    public string? GetDefinitionId(Guid cardInstanceId) =>
        GetCard(cardInstanceId)?.DefinitionId;

    public IReadOnlyList<string> ResolveDefinitionIds(IEnumerable<Guid> cardInstanceIds) =>
        cardInstanceIds
            .Select(id => GetDefinitionId(id) ?? throw new InvalidOperationException(
                $"Card zone contains unknown instance: {id}"))
            .ToImmutableArray();

    private void SetDefinitionProjection(
        ref ImmutableList<Guid> zone,
        IEnumerable<string>? definitionIds,
        string zoneName)
    {
        var ids = ImmutableList.CreateBuilder<Guid>();
        var index = 0UL;
        foreach (var definitionId in definitionIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(definitionId))
                continue;
            var instanceId = DeterministicId.Create(
                0,
                index++,
                $"deck-construction:{zoneName}:{definitionId}");
            ids.Add(instanceId);
            _cardInstances = _cardInstances.SetItem(instanceId, new CardInstanceState
            {
                CardInstanceId = instanceId,
                DefinitionId = definitionId
            });
        }
        zone = ids.ToImmutable();
    }
}
