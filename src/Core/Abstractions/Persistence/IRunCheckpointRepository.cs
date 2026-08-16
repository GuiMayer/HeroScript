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

    async Task<IReadOnlyList<RunJournalEntry>> LoadJournalAsync(
        Guid runId,
        int afterSequence = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        var checkpoints = await LoadCheckpointsAsync(runId, ct).ConfigureAwait(false);
        return checkpoints
            .Where(item => item.JournalEntry.Sequence > afterSequence)
            .OrderBy(item => item.JournalEntry.Sequence)
            .Take(limit)
            .Select(item => item.JournalEntry)
            .ToArray();
    }
}
