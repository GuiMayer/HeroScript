using System.Collections.Immutable;

namespace Core.Determinism;

/// <summary>
/// Immutable output of a domain transition. A transition receives state and
/// context and returns their replacements plus facts emitted by the operation.
/// </summary>
public sealed record TransitionResult<TState, TEvent>(
    TState State,
    DeterministicContext Context,
    ImmutableArray<TEvent> Events)
{
    public static TransitionResult<TState, TEvent> WithoutEvents(
        TState state,
        DeterministicContext context) =>
        new(state, context, ImmutableArray<TEvent>.Empty);
}
