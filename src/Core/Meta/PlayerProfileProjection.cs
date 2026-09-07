using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Determinism;

namespace Core.Meta;

public sealed record ProfileRunSummary
{
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public string ConfigName { get; init; } = "default";
    public string? ModeId { get; init; }
    public string? ChallengeId { get; init; }
    public string? CurrentNodeId { get; init; }
    public bool Completed { get; init; }
    public ulong Seed { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
}

public sealed record PlayerProfileProjection
{
    public string PlayerId { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public int TotalRuns { get; init; }
    public int CompletedRuns { get; init; }
    public int ActiveRuns { get; init; }
    public ImmutableArray<string> Unlocks { get; init; } = [];
    public ImmutableArray<string> Achievements { get; init; } = [];
    public ImmutableArray<ProfileRunSummary> Runs { get; init; } = [];
}

public interface IPlayerProfileProjectionReader
{
    Task<PlayerProfileProjection> ReadAsync(
        string playerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Rebuildable meta projection. It never mutates unlocks directly: all values
/// are derived from authoritative run snapshots and can be recomputed.
/// </summary>
public sealed class PlayerProfileProjectionReader : IPlayerProfileProjectionReader
{
    private readonly IRunCommitReader _runs;

    public PlayerProfileProjectionReader(IRunCommitReader runs)
    {
        _runs = runs;
    }

    public async Task<PlayerProfileProjection> ReadAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        var summaries = ImmutableArray.CreateBuilder<ProfileRunSummary>();
        foreach (var runId in (await _runs.ListRunIdsAsync(cancellationToken).ConfigureAwait(false))
                     .OrderBy(id => id))
        {
            var state = await _runs.LoadLatestStateAsync(runId, cancellationToken).ConfigureAwait(false);
            if (state == null || !string.Equals(state.PlayerEntityId, playerId, StringComparison.Ordinal))
                continue;
            if (state.Lineage?.InternalSimulation == true)
                continue;

            summaries.Add(new ProfileRunSummary
            {
                RunId = state.RunId,
                Sequence = state.Sequence,
                ConfigName = state.ConfigName,
                ModeId = state.ModeId,
                ChallengeId = state.ChallengeId,
                CurrentNodeId = state.CurrentNodeId,
                Completed = IsCompleted(state),
                Seed = state.Determinism.Seed,
                ContentRevision = state.Determinism.ContentRevision,
                StateHash = CanonicalJson.ComputeHash(state)
            });
        }

        var ordered = summaries
            .OrderByDescending(run => run.Sequence)
            .ThenBy(run => run.RunId)
            .ToImmutableArray();
        var completed = ordered.Count(run => run.Completed);
        var unlocks = completed > 0
            ? ImmutableArray.Create("completed-run-content")
            : ImmutableArray<string>.Empty;
        var achievements = ImmutableArray.CreateBuilder<string>();
        if (!ordered.IsEmpty)
            achievements.Add("first-run");
        if (completed > 0)
            achievements.Add("first-completion");

        var payload = new ProfileRevisionPayload(
            playerId,
            ordered.Length,
            completed,
            unlocks,
            achievements.ToImmutable(),
            ordered);
        return new PlayerProfileProjection
        {
            PlayerId = playerId,
            Revision = CanonicalJson.ComputeHash(payload),
            TotalRuns = ordered.Length,
            CompletedRuns = completed,
            ActiveRuns = ordered.Length - completed,
            Unlocks = unlocks,
            Achievements = payload.Achievements,
            Runs = ordered
        };
    }

    private static bool IsCompleted(Core.Run.RunState state)
    {
        if (state.ActiveEncounterId != null || state.CurrentNodeId == null)
            return false;
        var node = state.Map.Nodes.FirstOrDefault(item =>
            string.Equals(item.NodeId, state.CurrentNodeId, StringComparison.Ordinal));
        return node != null && node.NextNodeIds.Count == 0 &&
               state.Map.ResolvedNodeIds.Contains(node.NodeId, StringComparer.Ordinal);
    }

    private sealed record ProfileRevisionPayload(
        string PlayerId,
        int TotalRuns,
        int CompletedRuns,
        ImmutableArray<string> Unlocks,
        ImmutableArray<string> Achievements,
        ImmutableArray<ProfileRunSummary> Runs);
}
