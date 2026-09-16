using System.Collections.Immutable;
using Core.CardZones;
using Core.Determinism;
using Core.Run;

namespace Core.Combat.Flow;

internal sealed record EndDeckCycleResult(
    DeckState State,
    DeterministicContext Context,
    IReadOnlyList<string> AffectedInstanceIds,
    IReadOnlyList<string> Discarded,
    IReadOnlyList<string> Exhausted)
{
    public ImmutableArray<CardZoneFlowStepRecord> CardZoneSteps { get; init; } = [];
}

internal sealed record ResourceRefreshResult(
    DeckState State,
    DeterministicContext Context,
    IReadOnlyList<string> NewlyPlayableDefinitionIds)
{
    public ImmutableArray<CardZoneFlowStepRecord> CardZoneSteps { get; init; } = [];
}
