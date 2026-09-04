using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Core.Run;

public sealed record RunDefinition
{
    private ImmutableList<string> _startingDeck = [];
    private ImmutableList<RunMapNodeDefinition> _mapNodes = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;
    private ImmutableDictionary<string, float> _startingResources =
        ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);

    public string RunId { get; init; } = "default_run";
    public IReadOnlyDictionary<string, float> StartingResources
    {
        get => _startingResources;
        init => _startingResources = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, float>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    }
    [JsonIgnore]
    public int StartingGold
    {
        get => (int)_startingResources.GetValueOrDefault("gold");
        init => _startingResources = _startingResources.SetItem("gold", value);
    }
    [JsonIgnore]
    public int StartingPowerPoints
    {
        get => (int)_startingResources.GetValueOrDefault("power_points");
        init => _startingResources = _startingResources.SetItem("power_points", value);
    }
    public int StartingHandSize { get; init; } = 5;
    public IReadOnlyList<string> StartingDeck
    {
        get => _startingDeck;
        init => _startingDeck = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyList<RunMapNodeDefinition> MapNodes
    {
        get => _mapNodes;
        init => _mapNodes = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}

public sealed record RunMapNodeDefinition
{
    private ImmutableList<string> _nextNodeIds = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;

    public string NodeId { get; init; } = string.Empty;
    public string NodeType { get; init; } = "combat";
    public IReadOnlyList<string> NextNodeIds
    {
        get => _nextNodeIds;
        init => _nextNodeIds = value?.ToImmutableList() ?? [];
    }
    public IReadOnlyDictionary<string, object> Metadata
    {
        get => _metadata;
        init => _metadata = value?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase)
            ?? ImmutableDictionary<string, object>.Empty;
    }
}
