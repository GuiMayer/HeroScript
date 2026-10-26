using System.Collections.Immutable;

namespace Core.Run.DeckCompartments;

public sealed record ActorDeckCompartmentState
{
    public string ActorId { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public ImmutableSortedDictionary<Guid, string> Allocations { get; init; } = ImmutableSortedDictionary<Guid, string>.Empty;
}

/// <summary>Build association, independent from card circulation and upgrades. Absent unless enabled.</summary>
public sealed record DeckCompartmentState : Core.Determinism.ICanonicalHashMemoizable
{
    public ImmutableSortedDictionary<string, ActorDeckCompartmentState> Actors { get; init; } =
        ImmutableSortedDictionary<string, ActorDeckCompartmentState>.Empty.WithComparers(StringComparer.Ordinal);
}

public sealed record DeckCompartmentAllocation(Guid CardInstanceId, string CompartmentId);
public sealed record ApplyDeckCompartmentsCommand(string ActorId, ImmutableArray<DeckCompartmentAllocation> Allocations);
public sealed record DeckCompartmentChangePlan(RunState Candidate, ActorDeckCompartmentState Before, ActorDeckCompartmentState After);
