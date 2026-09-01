namespace Core.Events;

/// <summary>
/// Immutable observability metadata propagated independently from gameplay
/// state. None of these values participates in deterministic state hashes.
/// </summary>
public sealed record GameEventContext
{
    public static GameEventContext Empty { get; } = new();

    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public Guid? RunId { get; init; }
    public Guid? CombatId { get; init; }
    public Guid? CommandId { get; init; }
    public int? ExpectedRunSequence { get; init; }
    public ulong? ExpectedRunStep { get; init; }
    public string ContentRevision { get; init; } = string.Empty;

    public bool IsEmpty =>
        !CorrelationId.HasValue &&
        !CausationId.HasValue &&
        string.IsNullOrEmpty(TraceId) &&
        !RunId.HasValue &&
        !CombatId.HasValue &&
        !CommandId.HasValue &&
        !ExpectedRunSequence.HasValue &&
        !ExpectedRunStep.HasValue &&
        string.IsNullOrEmpty(ContentRevision);

    /// <summary>
    /// Applies non-empty values from <paramref name="overlay"/> on top of this
    /// context. This allows an HTTP trace, command and explicit event metadata
    /// to be composed through nested scopes.
    /// </summary>
    public GameEventContext Merge(GameEventContext? overlay)
    {
        if (overlay == null || overlay.IsEmpty)
            return this;

        return this with
        {
            CorrelationId = overlay.CorrelationId ?? CorrelationId,
            CausationId = overlay.CausationId ?? CausationId,
            TraceId = string.IsNullOrEmpty(overlay.TraceId) ? TraceId : overlay.TraceId,
            RunId = overlay.RunId ?? RunId,
            CombatId = overlay.CombatId ?? CombatId,
            CommandId = overlay.CommandId ?? CommandId,
            ExpectedRunSequence = overlay.ExpectedRunSequence ?? ExpectedRunSequence,
            ExpectedRunStep = overlay.ExpectedRunStep ?? ExpectedRunStep,
            ContentRevision = string.IsNullOrEmpty(overlay.ContentRevision)
                ? ContentRevision
                : overlay.ContentRevision
        };
    }
}

public interface IGameEventContextAccessor
{
    GameEventContext Current { get; }
    IDisposable Push(GameEventContext context);
}

/// <summary>
/// Async-flow-local context suitable for singleton services. Concurrent HTTP
/// requests and simulation tasks receive independent event metadata.
/// </summary>
public sealed class GameEventContextAccessor : IGameEventContextAccessor
{
    private readonly AsyncLocal<ContextHolder?> _current = new();

    public GameEventContext Current => _current.Value?.Context ?? GameEventContext.Empty;

    public IDisposable Push(GameEventContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = _current.Value;
        _current.Value = new ContextHolder(Current.Merge(context));
        return new ContextScope(this, previous);
    }

    private sealed record ContextHolder(GameEventContext Context);

    private sealed class ContextScope : IDisposable
    {
        private readonly GameEventContextAccessor _owner;
        private readonly ContextHolder? _previous;
        private int _disposed;

        public ContextScope(GameEventContextAccessor owner, ContextHolder? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _owner._current.Value = _previous;
        }
    }
}
