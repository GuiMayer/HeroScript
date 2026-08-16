namespace Core.Abstractions.Persistence;

/// <summary>
/// Persistência atômica de snapshot e entrada de journal no mesmo artefato.
/// </summary>
public interface IRunCheckpointRepository : IRunStateRepository
{
    Task SaveCheckpointAsync(RunCheckpoint checkpoint, CancellationToken ct = default);
    Task<IReadOnlyList<RunCheckpoint>> LoadCheckpointsAsync(
        Guid runId,
        CancellationToken ct = default);
}
