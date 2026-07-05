using Core.Run;

namespace Core.Abstractions.Persistence;

/// <summary>
/// Repository for persisting and restoring run state snapshots across process restarts.
/// Supports versioned snapshots (append-only, immutable).
/// Implementations must be thread-safe.
/// </summary>
public interface IRunStateRepository
{
    /// <summary>Persists a new run state snapshot with incremented sequence.</summary>
    Task SaveAsync(RunState state, CancellationToken ct = default);

    /// <summary>Loads the latest run state snapshot by ID. Returns null if not found.</summary>
    Task<RunState?> LoadLatestAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Loads a specific run state snapshot by ID and sequence. Returns null if not found.</summary>
    Task<RunState?> LoadAsync(Guid runId, int sequence, CancellationToken ct = default);

    /// <summary>Lists all available snapshot sequences for a run.</summary>
    Task<IReadOnlyList<int>> ListSnapshotsAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Deletes all snapshots for a run.</summary>
    Task DeleteAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Lists all persisted run IDs.</summary>
    Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default);
}
