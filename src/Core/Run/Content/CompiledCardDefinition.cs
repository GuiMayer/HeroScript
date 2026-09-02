using System.Collections.Immutable;

namespace Core.Run.Content;

/// <summary>
/// Fully expanded immutable card container. Runtime resolution consumes this
/// shape and never follows bundle references while a run is active.
/// </summary>
public sealed record CompiledCardDefinition
{
    private ImmutableArray<CardComponentDefinition> _components = [];
    private ImmutableArray<string> _tags = [];

    public string CardId { get; init; } = string.Empty;
    public CardRarity Rarity { get; init; }
    public int BaseGoldPrice { get; init; }
    public int DecomposePowerPoints { get; init; }
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
