using Core.Run;

namespace Core.Abstractions.Persistence;

public interface IRunCommitReader
{
    Task<RunCommit?> LoadCommitAsync(Guid runId, int sequence, CancellationToken ct = default);
    Task<IReadOnlyList<RunCommit>> LoadCommitsAsync(Guid runId, CancellationToken ct = default);
    async Task<RunCommit?> FindCommandAsync(Guid runId, Guid commandId, CancellationToken ct = default) =>
        (await LoadCommitsAsync(runId, ct).ConfigureAwait(false))
            .LastOrDefault(commit => commit.RootCommand.CommandId == commandId);
    Task<RunState?> LoadStateAsync(Guid runId, int sequence, CancellationToken ct = default);
    Task<RunState?> LoadLatestStateAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> ListCommitSequencesAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default);
}

public interface IRunCommitStore : IRunCommitReader
{
    Task<RunCommitAppendResult> AppendAsync(RunCommit commit, CancellationToken ct = default);
    Task DeleteRunAsync(Guid runId, CancellationToken ct = default);
}
