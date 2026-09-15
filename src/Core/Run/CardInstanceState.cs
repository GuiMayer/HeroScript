using System.Collections.Immutable;
using Core.CardZones;
using Core.Run.Content;

namespace Core.Run;

public enum CardInstancePersistence
{
    Run,
    Encounter
}

/// <summary>
/// Immutable identity of one physical card owned by a run. Definition identity
/// and instance identity intentionally never share the same field.
/// </summary>
public sealed record CardInstanceState
{
    private ImmutableArray<CardUpgradeState> _upgrades = [];

    public Guid CardInstanceId { get; init; }
    public string DefinitionId { get; init; } = string.Empty;
    public string OwnerId { get; init; } = "$run";
    public ulong CreationOrdinal { get; init; }
    public CardInstanceLifetimeDefinition Lifetime { get; init; } = new();
    public CardInstancePersistence Persistence { get; init; } = CardInstancePersistence.Run;

    public IReadOnlyList<CardUpgradeState> Upgrades
    {
        get => _upgrades;
        init => _upgrades = value?.ToImmutableArray() ?? [];
    }

    internal ImmutableArray<CardUpgradeState> UpgradeItems => _upgrades;
}

public sealed record CardUpgradeState
{
    private ImmutableArray<CardUpgradePatchDefinition> _patches = [];

    public string UpgradeId { get; init; } = string.Empty;

    public IReadOnlyList<CardUpgradePatchDefinition> Patches
    {
        get => _patches;
        init => _patches = value?.ToImmutableArray() ?? [];
    }
}

public sealed record CardUpgradeDefinition
{
    private ImmutableArray<string> _cardDefinitionIds = [];
    private ImmutableArray<CardUpgradePatchDefinition> _patches = [];

    public string UpgradeId { get; init; } = string.Empty;
    public int MaxApplications { get; init; } = 1;

    public IReadOnlyList<string> CardDefinitionIds
    {
        get => _cardDefinitionIds;
        init => _cardDefinitionIds = value?.ToImmutableArray() ?? [];
    }

    public IReadOnlyList<CardUpgradePatchDefinition> Patches
    {
        get => _patches;
        init => _patches = value?.ToImmutableArray() ?? [];
    }

    public bool AppliesTo(string definitionId) =>
        _cardDefinitionIds.IsEmpty || _cardDefinitionIds.Contains(definitionId, StringComparer.Ordinal);
}
