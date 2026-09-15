using System.Collections.Immutable;
using Core.Effects;
using Core.CardZones;

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
    public int StartingHandSize { get; init; } = 5;
    public string? InitialCardZoneId { get; init; }
    public CardZoneOwnerBinding InitialCardOwner { get; init; }
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
    private ImmutableArray<EffectDefinition> _entryEffects = [];
    private ImmutableArray<EffectDefinition> _exitEffects = [];

    public string NodeId { get; init; } = string.Empty;
    public RunActivityDefinition Activity { get; init; } = new();
    public RunActivityCompletionPolicy CompletionPolicy { get; init; } = RunActivityCompletionPolicy.Required;
    public IReadOnlyList<EffectDefinition> EntryEffects
    {
        get => _entryEffects;
        init => _entryEffects = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<EffectDefinition> ExitEffects
    {
        get => _exitEffects;
        init => _exitEffects = value?.ToImmutableArray() ?? [];
    }
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
