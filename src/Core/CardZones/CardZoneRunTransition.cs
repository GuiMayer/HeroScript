using System.Collections.Immutable;
using Core.Determinism;
using Core.Run;

namespace Core.CardZones;

public sealed record CardZoneRunTransition(
    DeckState State,
    DeterministicContext Context,
    ImmutableArray<string> CardDefinitionIds);
