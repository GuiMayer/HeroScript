using System.Collections.Immutable;
using System.Text.Json;

namespace Core.Combat.Models;

/// <summary>
/// Serializable TCG board projection. The command layer does not mutate these
/// collections in place; future play/response transitions must replace them.
/// </summary>
public sealed record CombatBoardState
{
    private ImmutableDictionary<string, CombatZoneState> _zones =
        ImmutableDictionary<string, CombatZoneState>.Empty.WithComparers(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, CombatZoneState> Zones
    {
        get => _zones;
        init => _zones = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, CombatZoneState>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record CombatZoneState
{
    private ImmutableArray<BoardObjectState> _objects = [];

    public string ZoneId { get; init; } = string.Empty;
    public string? OwnerId { get; init; }
    public bool Public { get; init; } = true;

    public IReadOnlyList<BoardObjectState> Objects
    {
        get => _objects;
        init => _objects = value?.ToImmutableArray() ?? [];
    }
}

public sealed record BoardObjectState
{
    private ImmutableDictionary<string, JsonElement> _counters =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public Guid ObjectInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string ControllerId { get; init; } = string.Empty;
    public bool Exhausted { get; init; }

    public IReadOnlyDictionary<string, JsonElement> Counters
    {
        get => _counters;
        init => _counters = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}
