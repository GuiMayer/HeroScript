using Core.Run;

namespace Core.Abstractions.Persistence;

/// <summary>
/// Repository for persisting and restoring run state across process restarts.
/// Implementations must be thread-safe.
/// </summary>
public interface IRunStateRepository
{
    /// <summary>Persists (or updates) a run state snapshot.</summary>
    Task SaveAsync(RunState state, CancellationToken ct = default);

    /// <summary>Loads a run state by ID. Returns null if not found.</summary>
    Task<RunState?> LoadAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Deletes a persisted run state.</summary>
    Task DeleteAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Lists all persisted run IDs.</summary>
    Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default);
}
