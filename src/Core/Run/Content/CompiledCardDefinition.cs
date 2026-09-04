using System.Collections.Immutable;
using Core.Resources;

namespace Core.Run.Content;

/// <summary>
/// Fully expanded immutable card container. Runtime resolution consumes this
/// shape and never follows bundle references while a run is active.
/// </summary>
public sealed record CompiledCardDefinition
{
    private ImmutableArray<CardComponentDefinition> _components = [];
    private ImmutableArray<string> _tags = [];
    private ImmutableArray<ResourceAmount> _basePrices = [];
    private ImmutableArray<ResourceAmount> _decomposeRewards = [];

    public string CardId { get; init; } = string.Empty;
    public CardRarity Rarity { get; init; }
    public IReadOnlyList<ResourceAmount> BasePrices
    {
        get => _basePrices;
        init => _basePrices = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<ResourceAmount> DecomposeRewards
    {
        get => _decomposeRewards;
        init => _decomposeRewards = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyList<CardComponentDefinition> Components
    {
        get => _components;
        init => _components = value?.ToImmutableArray() ?? [];
    }
    public string Fingerprint { get; init; } = string.Empty;

    public T? SingleOrDefault<T>() where T : CardComponentDefinition =>
        _components.OfType<T>().SingleOrDefault();

    public IReadOnlyList<T> All<T>() where T : CardComponentDefinition =>
        _components.OfType<T>().ToImmutableArray();
}
