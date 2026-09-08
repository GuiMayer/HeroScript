namespace Core.Combat.TurnPhase;

/// <summary>
/// Lightweight cursor over phase content pinned by the run revision.
/// Definitions never live inside gameplay snapshots.
/// </summary>
public sealed record PhaseState
{
    public string SequenceId { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public string Cursor { get; init; } = string.Empty;
}
