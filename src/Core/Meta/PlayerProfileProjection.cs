using System.Collections.Immutable;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run;

namespace Core.Meta;

public sealed record ProfileRunSummary
{
    public Guid RunId { get; init; }
    public int Sequence { get; init; }
    public string ConfigName { get; init; } = "default";
    public string SettingId { get; init; } = string.Empty;
    public string? ModeId { get; init; }
    public string? ChallengeId { get; init; }
    public string? CurrentNodeId { get; init; }
    public bool Completed { get; init; }
    public Core.Run.RunLifecycleState Lifecycle { get; init; }
    public ulong Seed { get; init; }
    public string ContentRevision { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
}

public sealed record PlayerProfileProjection
{
    public string PlayerId { get; init; } = string.Empty;
    public string SettingId { get; init; } = string.Empty;
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
        string settingId,
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
        string settingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingId);
        var summaries = ImmutableArray.CreateBuilder<ProfileRunSummary>();
        foreach (var runId in (await _runs.ListRunIdsAsync(cancellationToken).ConfigureAwait(false))
                     .OrderBy(id => id))
        {
            RunState? state;
            try
            {
                state = await _runs.LoadLatestStateAsync(runId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or IOException)
            {
                // History is a projection over independent immutable streams. One unreadable
                // stream must never hide valid journeys, and it is never migrated implicitly.
                continue;
            }
            if (state == null || !string.Equals(state.PlayerEntityId, playerId, StringComparison.Ordinal))
                continue;
            // Partition before deriving statistics, unlocks, achievements and hashes.
            // Runs keep their recorded setting/revision; selecting a setting is not migration.
            if (!string.Equals(state.SettingId, settingId, StringComparison.Ordinal))
                continue;
            if (state.Lineage?.InternalSimulation == true)
                continue;

            summaries.Add(new ProfileRunSummary
            {
                RunId = state.RunId,
                Sequence = state.Sequence,
                ConfigName = state.ConfigName,
                SettingId = state.SettingId,
                ModeId = state.ModeId,
                ChallengeId = state.ChallengeId,
                CurrentNodeId = state.CurrentNodeId,
                Completed = IsCompleted(state),
                Lifecycle = state.Lifecycle,
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
            settingId,
            ordered.Length,
            completed,
            unlocks,
            achievements.ToImmutable(),
            ordered);
        return new PlayerProfileProjection
        {
            PlayerId = playerId,
            SettingId = settingId,
            Revision = CanonicalJson.ComputeHash(payload),
            TotalRuns = ordered.Length,
            CompletedRuns = completed,
            ActiveRuns = ordered.Count(run => run.Lifecycle == Core.Run.RunLifecycleState.Active),
            Unlocks = unlocks,
            Achievements = payload.Achievements,
            Runs = ordered
        };
    }

    private static bool IsCompleted(Core.Run.RunState state) =>
        state.Lifecycle == Core.Run.RunLifecycleState.Completed;

    private sealed record ProfileRevisionPayload(
        string PlayerId,
        string SettingId,
        int TotalRuns,
        int CompletedRuns,
        ImmutableArray<string> Unlocks,
        ImmutableArray<string> Achievements,
        ImmutableArray<ProfileRunSummary> Runs);
}
