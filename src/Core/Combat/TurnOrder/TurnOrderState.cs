using System.Collections.Immutable;

namespace Core.Combat.TurnOrder;

/// <summary>
/// Serializable strategy state stored in every combat snapshot. Generic scores
/// and seeded ranks are shared by all strategies; rolls and gauges preserve the
/// exact state required by initiative and ATB.
/// </summary>
public sealed record TurnOrderState
{
    private ImmutableArray<string> _order = [];
    private ImmutableSortedDictionary<string, float> _scores = EmptyFloatMap();
    private ImmutableSortedDictionary<string, int> _initiativeRolls = EmptyIntMap();
    private ImmutableSortedDictionary<string, float> _atbGauges = EmptyFloatMap();
    private ImmutableSortedDictionary<string, int> _tieBreakRanks = EmptyIntMap();

    public TurnOrderStrategy Strategy { get; init; }
    public string PolicyFingerprint { get; init; } = string.Empty;
    public TurnOrderRecalculationBoundary LastBoundary { get; init; }
    public ulong Epoch { get; init; }
    public ulong ContinuousTick { get; init; }
    public ulong TieBreakGeneration { get; init; }
    public TurnOrderTieBreakEpoch TieBreakEpoch { get; init; }
    public IReadOnlyList<string> Order
    {
        get => _order;
        init => _order = value?.ToImmutableArray() ?? [];
    }
    public IReadOnlyDictionary<string, float> Scores
    {
        get => _scores;
        init => _scores = value?.ToImmutableSortedDictionary(StringComparer.Ordinal) ?? EmptyFloatMap();
    }
    public IReadOnlyDictionary<string, int> InitiativeRolls
    {
        get => _initiativeRolls;
        init => _initiativeRolls = value?.ToImmutableSortedDictionary(StringComparer.Ordinal) ?? EmptyIntMap();
    }
    public IReadOnlyDictionary<string, float> AtbGauges
    {
        get => _atbGauges;
        init => _atbGauges = value?.ToImmutableSortedDictionary(StringComparer.Ordinal) ?? EmptyFloatMap();
    }
    public IReadOnlyDictionary<string, int> TieBreakRanks
    {
        get => _tieBreakRanks;
        init => _tieBreakRanks = value?.ToImmutableSortedDictionary(StringComparer.Ordinal) ?? EmptyIntMap();
    }

    private static ImmutableSortedDictionary<string, float> EmptyFloatMap() =>
        ImmutableSortedDictionary<string, float>.Empty.WithComparers(StringComparer.Ordinal);

    private static ImmutableSortedDictionary<string, int> EmptyIntMap() =>
        ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
}
