namespace API.Contracts;

/// <summary>
/// Metadata shared by successful deterministic command responses.
/// </summary>
public sealed record CommandResultEnvelope<TState>
{
    /// <summary>Idempotency key supplied by the client.</summary>
    public Guid CommandId { get; init; }
    /// <summary>Persisted sequence after the accepted transition.</summary>
    public int? Sequence { get; init; }
    /// <summary>Deterministic step after the accepted transition.</summary>
    public ulong Step { get; init; }
    /// <summary>Canonical hash of the state before the transition.</summary>
    public string PreviousStateHash { get; init; } = string.Empty;
    /// <summary>Canonical hash of the resulting state.</summary>
    public string StateHash { get; init; } = string.Empty;
    /// <summary>Immutable aggregate read model after the transition.</summary>
    public TState State { get; init; } = default!;
    /// <summary>Events projected from the accepted transition.</summary>
    public IReadOnlyList<object> Events { get; init; } = [];
}
