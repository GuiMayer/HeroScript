using System.Collections.Immutable;

namespace Core.Run.DeckLoadouts;

/// <summary>Associations only. The topology remains the sole owner of card instances and upgrades.</summary>
public sealed record ActorDeckLoadoutState
{
    public string ActorId { get; init; } = string.Empty;
    public string PolicyId { get; init; } = string.Empty;
    public ImmutableSortedDictionary<string, ImmutableArray<Guid>> Groups { get; init; } =
        ImmutableSortedDictionary.Create<string, ImmutableArray<Guid>>(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, string> Modules { get; init; } =
        ImmutableSortedDictionary.Create<string, string>(StringComparer.Ordinal);
}

public sealed record DeckLoadoutState : Core.Determinism.ICanonicalHashMemoizable
{
    public ImmutableSortedDictionary<string, ActorDeckLoadoutState> Actors { get; init; } =
        ImmutableSortedDictionary.Create<string, ActorDeckLoadoutState>(StringComparer.Ordinal);
}

public sealed record DeckLoadoutSelection(
    ImmutableSortedDictionary<string, ImmutableArray<Guid>> Groups,
    ImmutableSortedDictionary<string, string> Modules);

public sealed record DeckLoadoutChangePlan(RunState Candidate, ActorDeckLoadoutState Before, ActorDeckLoadoutState After);

public sealed record CapturedDeckLoadout(string ActorId, string PolicyId, string ContentRevision, int SourceSequence,
    ulong SourceStep, ImmutableSortedDictionary<string, ImmutableArray<Guid>> Groups,
    ImmutableSortedDictionary<string, string> Modules, ImmutableArray<Guid> ActiveCardIds);
