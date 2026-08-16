namespace API.Contracts;

/// <summary>
/// Metadata shared by successful deterministic command responses.
/// </summary>
public sealed record CommandResultEnvelope<TState>
{
    public Guid CommandId { get; init; }
    public int? Sequence { get; init; }
    public ulong Step { get; init; }
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public TState State { get; init; } = default!;
    public IReadOnlyList<object> Events { get; init; } = [];
}
