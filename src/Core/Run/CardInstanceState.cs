using System.Collections.Immutable;
using System.Text.Json;

namespace Core.Run;

/// <summary>
/// Immutable identity of one physical card owned by a run. Definition identity
/// and instance identity intentionally never share the same field.
/// </summary>
public sealed record CardInstanceState
{
    private ImmutableArray<CardUpgradeState> _upgrades = [];

    public Guid CardInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;

    public IReadOnlyList<CardUpgradeState> Upgrades
    {
        get => _upgrades;
        init => _upgrades = value?.ToImmutableArray() ?? [];
    }

    internal ImmutableArray<CardUpgradeState> UpgradeItems => _upgrades;
}

public sealed record CardUpgradeState
{
    private ImmutableDictionary<string, JsonElement> _deltas =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public string UpgradeId { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, JsonElement> Deltas
    {
        get => _deltas;
        init => _deltas = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }
}

public sealed record CardUpgradeDefinition
{
    private ImmutableArray<string> _cardDefinitionIds = [];
    private ImmutableDictionary<string, JsonElement> _deltas =
        ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);

    public string UpgradeId { get; init; } = string.Empty;
    public int MaxApplications { get; init; } = 1;

    public IReadOnlyList<string> CardDefinitionIds
    {
        get => _cardDefinitionIds;
        init => _cardDefinitionIds = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyDictionary<string, JsonElement> Deltas
    {
        get => _deltas;
        init => _deltas = value?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, JsonElement>.Empty.WithComparers(StringComparer.Ordinal);
    }

    public bool AppliesTo(string definitionId) =>
        _cardDefinitionIds.IsEmpty || _cardDefinitionIds.Contains(definitionId, StringComparer.Ordinal);
}
