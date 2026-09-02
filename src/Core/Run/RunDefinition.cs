using System.Collections.Immutable;

namespace Core.Run;

public sealed record RunDefinition
{
    private ImmutableList<string> _startingDeck = [];
    private ImmutableList<RunMapNodeDefinition> _mapNodes = [];
    private ImmutableDictionary<string, object> _metadata = ImmutableDictionary<string, object>.Empty;

    public string RunId { get; init; } = "default_run";
    public int StartingGold { get; init; }
    public int StartingPowerPoints { get; init; }
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
