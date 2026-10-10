using Core.Caching;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Meta;
using Core.Run;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Serializes contribution-bearing appends across runs and processes for one player/setting.
/// The lease contains only a cache generation, never progress. The run commit is the sole authority.
/// Gameplay-only commands bypass the profile lease and do not scan historical streams.
/// </summary>
public sealed class ProfileProgressCommitCoordinator : IProfileProgressSnapshotReader
{
    private readonly IRunCommitReader _inner;
    private readonly Func<PreparedRunCommit, CancellationToken, Task<RunCommitAppendResult>> _append;
    private readonly string _leaseDirectory;
    private readonly LruCache<string, CachedProfile> _cache = new(256);
    private sealed record CachedProfile(string Generation, ProfileProgressSnapshot Snapshot);

    public ProfileProgressCommitCoordinator(IRunCommitReader inner, string storePath,
        Func<PreparedRunCommit, CancellationToken, Task<RunCommitAppendResult>> append)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _append = append ?? throw new ArgumentNullException(nameof(append));
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _leaseDirectory = Path.Combine(Path.GetFullPath(storePath), "_profile-progress");
    }

    public Task<RunCommitAppendResult> AppendAsync(RunCommit commit, CancellationToken ct = default) =>
        AppendPreparedAsync(PreparedRunCommit.Create(commit), ct);

    public async Task<RunCommitAppendResult> AppendPreparedAsync(PreparedRunCommit prepared, CancellationToken ct = default)
    {
        // Callers cannot assert grants or profile counters; only canonical transition extraction can.
        if (prepared.Commit.ProfileProgress != null)
            throw new InvalidOperationException("Profile proofs are assigned by the canonical commit coordinator");
        var state = prepared.LiveState;
        if (state.ResolvedMode?.ProfileProgressPolicy == null || state.Lineage?.InternalSimulation == true)
            return await AppendInnerAsync(prepared, ct).ConfigureAwait(false);
        var before = prepared.PreviousState ?? (state.Sequence > 1
            ? await _inner.LoadStateAsync(state.RunId, state.Sequence - 1, ct).ConfigureAwait(false) : null);
        var facts = ProfileProgressFacts.Extract(before, state, prepared.Commit.RootCommand);
        if (facts.IsEmpty) return await AppendInnerAsync(prepared, ct).ConfigureAwait(false);

        var scope = Scope(state.PlayerEntityId, state.SettingId);
        await using var lease = await AcquireAsync(scope, ct).ConfigureAwait(false);
        // Check retries BEFORE reading/reducing the new profile basis. The original proof never changes.
        var existing = await _inner.FindCommandAsync(state.RunId, prepared.Commit.RootCommand.CommandId, ct).ConfigureAwait(false);
        if (existing != null)
        {
            if (CanonicalJson.ComputeHash(existing with { ProfileProgress = null, StateAfter = null }) !=
                CanonicalJson.ComputeHash(prepared.Commit with { StateAfter = null }))
                throw new InvalidOperationException("Run command collision during profile contribution");
            return new(existing, true);
        }
        var basis = await ReadUnderLeaseAsync(state.PlayerEntityId, state.SettingId, scope, lease, ct).ConfigureAwait(false);
        var planned = ProfileProgressReducer.Plan(basis, new()
        {
            PlayerId = state.PlayerEntityId, SettingId = state.SettingId, ProfileSequence = checked(basis.Sequence + 1),
            BasisRevision = basis.Revision, RunId = state.RunId, RunSequence = state.Sequence,
            CommandId = prepared.Commit.RootCommand.CommandId, ContentRevision = state.Determinism.ContentRevision,
            Policy = state.ResolvedMode.ProfileProgressPolicy, Contributions = facts
        });
        if (planned.IsFailure) throw new InvalidOperationException(planned.Error);
        if (planned.Value == null) return await AppendInnerAsync(prepared, ct).ConfigureAwait(false);
        var applied = ProfileProgressReducer.Apply(basis, planned.Value);
        if (applied.IsFailure) throw new InvalidOperationException(applied.Error);
        // Invalidate OTHER processes before the atomic append. A crash cannot leave their cache valid.
        var generation = Guid.NewGuid().ToString("N"); // nondeterministic-boundary: operational cache token; never part of a proof or gameplay state.
        var bytes = System.Text.Encoding.ASCII.GetBytes(generation);
        lease.Position = 0;
        await lease.WriteAsync(bytes, ct).ConfigureAwait(false);
        lease.SetLength(bytes.Length);
        lease.Flush(flushToDisk: true);
        try
        {
            var result = await AppendInnerAsync(prepared.WithProfileProgress(planned.Value), ct).ConfigureAwait(false);
            _cache.Set(scope, new(Convert.ToHexString(bytes), applied.Value));
            return result;
        }
        catch { _cache.Remove(scope); throw; }
    }

    public async Task<ProfileProgressSnapshot> ReadAsync(string playerId, string settingId, CancellationToken ct = default)
    {
        var scope = Scope(playerId, settingId);
        await using var lease = await AcquireAsync(scope, ct).ConfigureAwait(false);
        return await ReadUnderLeaseAsync(playerId, settingId, scope, lease, ct).ConfigureAwait(false);
    }

    private async Task<ProfileProgressSnapshot> ReadUnderLeaseAsync(
        string player, string setting, string scope, FileStream lease, CancellationToken ct)
    {
        lease.Position = 0;
        var bytes = new byte[32];
        var length = await lease.ReadAsync(bytes, ct).ConfigureAwait(false);
        var generation = Convert.ToHexString(bytes.AsSpan(0, length));
        // The persisted marker and the in-memory value use the same representation.
        if (_cache.TryGetValue(scope, out var cached) && cached != null && cached.Generation == generation) return cached.Snapshot;
        var proofs = _inner is IProfileProgressCommitReader meta
            ? (await meta.LoadProfileProgressCommitsAsync(player, setting, ct).ConfigureAwait(false)).ToList()
            : new List<ProfileProgressCommit>();
        foreach (var runId in _inner is IProfileProgressCommitReader ? [] : await _inner.ListRunIdsAsync(ct).ConfigureAwait(false))
        {
            IReadOnlyList<RunCommit> commits;
            try
            {
                var initial = await _inner.LoadCommitAsync(runId, 1, ct).ConfigureAwait(false);
                if (initial == null || initial.RequireState().PlayerEntityId != player ||
                    initial.RequireState().SettingId != setting || initial.RequireState().Lineage?.InternalSimulation == true)
                    continue;
                commits = _inner is IRunCommitEnvelopeReader envelopes
                    ? await envelopes.LoadCommitEnvelopesAsync(runId, ct).ConfigureAwait(false)
                    : await _inner.LoadCommitsAsync(runId, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Unsupported run commit ", StringComparison.Ordinal))
            {
                // Old execution versions are not migrated or reinterpreted by a new profile policy.
                continue;
            }
            proofs.AddRange(commits.Where(commit => commit.ProfileProgress?.PlayerId == player &&
                commit.ProfileProgress.SettingId == setting).Select(commit => commit.ProfileProgress!));
        }
        var rebuilt = ProfileProgressReducer.Rebuild(player, setting, proofs);
        if (rebuilt.IsFailure) throw new InvalidDataException(rebuilt.Error);
        _cache.Set(scope, new(generation, rebuilt.Value));
        return rebuilt.Value;
    }

    private static string Scope(string player, string setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(player);
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        return CanonicalJson.ComputeHash(new { player, setting });
    }

    private async Task<FileStream> AcquireAsync(string scope, CancellationToken ct)
    {
        Directory.CreateDirectory(_leaseDirectory);
        var path = Path.Combine(_leaseDirectory, scope + ".lease");
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous); }
            catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
    }

    private Task<RunCommitAppendResult> AppendInnerAsync(PreparedRunCommit prepared, CancellationToken ct) =>
        _append(prepared, ct);
}
