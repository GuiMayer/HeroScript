using Core.Abstractions.Persistence;

namespace Core.Run.Projections;

public static class RunCommitScopes
{
    public const string Run = "run";
    public const string Combat = "combat";
}

public static class RunCommitScope
{
    public static bool ContainsCombat(RunCommit commit, Guid combatId) =>
        commit.Frames.Any(frame =>
            string.Equals(frame.Scope, RunCommitScopes.Combat, StringComparison.Ordinal) &&
            frame.CombatId == combatId) ||
        commit.Facts.Any(fact =>
            string.Equals(fact.Scope, RunCommitScopes.Combat, StringComparison.Ordinal) &&
            fact.CombatId == combatId);

    public static Guid? PrimaryCombatId(RunCommit commit) =>
        commit.Frames
            .Where(frame => string.Equals(frame.Scope, RunCommitScopes.Combat, StringComparison.Ordinal))
            .Select(frame => frame.CombatId)
            .FirstOrDefault(id => id.HasValue) ??
        commit.Facts
            .Where(fact => string.Equals(fact.Scope, RunCommitScopes.Combat, StringComparison.Ordinal))
            .Select(fact => fact.CombatId)
            .FirstOrDefault(id => id.HasValue);
}

public interface IRunCommitProjectionReader
{
    Task<IReadOnlyList<RunCommit>> ReadRunAsync(
        Guid runId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunCommit>> ReadCombatAsync(
        Guid runId,
        Guid combatId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared query boundary for projections built from authoritative commits.
/// Scope is data carried by frames and facts, never inferred from command names.
/// </summary>
public sealed class RunCommitProjectionReader : IRunCommitProjectionReader
{
    private readonly IRunCommitReader _commits;

    public RunCommitProjectionReader(IRunCommitReader commits)
    {
        _commits = commits;
    }

    public async Task<IReadOnlyList<RunCommit>> ReadRunAsync(
        Guid runId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateCursor(afterSequence, limit);
        return await _commits.LoadCommitsAsync(
            runId,
            afterSequence,
            limit,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RunCommit>> ReadCombatAsync(
        Guid runId,
        Guid combatId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateCursor(afterSequence, limit);
        var matches = new List<RunCommit>(System.Math.Min(limit, 128));
        var cursor = afterSequence;
        var batchSize = System.Math.Max(32, System.Math.Min(limit, 256));
        while (matches.Count < limit)
        {
            var batch = await _commits.LoadCommitsAsync(
                runId,
                cursor,
                batchSize,
                cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
                break;
            foreach (var commit in batch)
            {
                if (RunCommitScope.ContainsCombat(commit, combatId))
                    matches.Add(commit);
                if (matches.Count == limit)
                    break;
            }
            cursor = batch[^1].Sequence;
            if (batch.Count < batchSize)
                break;
        }
        return matches;
    }

    private static void ValidateCursor(int afterSequence, int limit)
    {
        if (afterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
