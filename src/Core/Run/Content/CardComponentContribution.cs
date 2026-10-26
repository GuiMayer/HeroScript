using System.Collections.Immutable;

namespace Core.Run.Content;

/// <summary>A closed structural source. No knowledge of equipment, compartments or game-specific tags.</summary>
public sealed record CardComponentContribution
{
    public string SourceId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public ImmutableArray<CardComponentDefinition> Components { get; init; } = [];
    public ImmutableArray<string> Tags { get; init; } = [];
}

public sealed record CardComponentContributionTrace(string SourceId, string ContentRevision,
    ImmutableArray<string> ComponentIds, ImmutableArray<string> Tags)
{
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
}
