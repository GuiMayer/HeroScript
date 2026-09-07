using System.Collections.Immutable;
using Core.Abstractions.Persistence;

namespace Core.Run.Branching;

public sealed record RunLineageEntry(Guid RunId, RunLineage Lineage);

public interface IRunLineageIndex
{
    Task<RunLineageEntry?> GetAsync(Guid runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunLineageEntry>> GetChildrenAsync(
        Guid parentRunId,
        bool includeInternalSimulations = false,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunLineageEntry>> GetDescendantsAsync(
        Guid rootRunId,
        bool includeInternalSimulations = true,
        CancellationToken cancellationToken = default);
    Task<int> CountDescendantsAsync(Guid rootRunId, CancellationToken cancellationToken = default);
    Task IndexAsync(RunCommit initialCommit, CancellationToken cancellationToken = default);
    Task RebuildAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Rebuildable in-memory projection of first-commit lineage. Queries never scan
/// latest run snapshots; startup/recovery can deterministically rebuild it from
/// authoritative commits.
/// </summary>
public sealed class RunLineageIndex : IRunLineageIndex, IDisposable
{
    private readonly IRunCommitReader _commits;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ImmutableDictionary<Guid, RunLineageEntry> _entries =
        ImmutableDictionary<Guid, RunLineageEntry>.Empty;
    private bool _initialized;

    public RunLineageIndex(IRunCommitReader commits) =>
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));

    public async Task<RunLineageEntry?> GetAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (_entries.TryGetValue(runId, out var entry))
            return entry;

        var initial = await _commits.LoadCommitAsync(runId, 1, cancellationToken).ConfigureAwait(false);
        if (initial?.Lineage == null)
            return null;
        await IndexAsync(initial, cancellationToken).ConfigureAwait(false);
        return _entries.GetValueOrDefault(runId);
    }

    public async Task<IReadOnlyList<RunLineageEntry>> GetChildrenAsync(
        Guid parentRunId,
        bool includeInternalSimulations = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return _entries.Values
            .Where(entry => entry.Lineage.ParentRunId == parentRunId)
            .Where(entry => includeInternalSimulations || !entry.Lineage.InternalSimulation)
            .OrderBy(entry => entry.Lineage.SourceSequence)
            .ThenBy(entry => entry.Lineage.BranchKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.RunId)
            .ToArray();
    }

    public async Task<IReadOnlyList<RunLineageEntry>> GetDescendantsAsync(
        Guid rootRunId,
        bool includeInternalSimulations = true,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return _entries.Values
            .Where(entry => entry.RunId != rootRunId && entry.Lineage.RootRunId == rootRunId)
            .Where(entry => includeInternalSimulations || !entry.Lineage.InternalSimulation)
            .OrderBy(entry => entry.Lineage.SourceSequence)
            .ThenBy(entry => entry.Lineage.BranchKey, StringComparer.Ordinal)
            .ThenBy(entry => entry.RunId)
            .ToArray();
    }

    public async Task<int> CountDescendantsAsync(
        Guid rootRunId,
        CancellationToken cancellationToken = default) =>
        (await GetDescendantsAsync(rootRunId, true, cancellationToken).ConfigureAwait(false)).Count;

    public async Task IndexAsync(RunCommit initialCommit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialCommit);
        if (initialCommit.Sequence != 1 || initialCommit.Lineage == null)
            throw new InvalidOperationException("Lineage index accepts only initial commits with lineage");
        var validation = initialCommit.Lineage.Validate(initialCommit.RunId);
        if (validation.IsFailure)
            throw new InvalidOperationException(validation.Error);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = new RunLineageEntry(initialCommit.RunId, initialCommit.Lineage);
            if (_entries.TryGetValue(initialCommit.RunId, out var existing) && existing != entry)
                throw new InvalidOperationException($"Run lineage collision: {initialCommit.RunId}");
            var candidate = _entries.SetItem(initialCommit.RunId, entry);
            ValidateGraph(candidate);
            _entries = candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        var rebuilt = ImmutableDictionary.CreateBuilder<Guid, RunLineageEntry>();
        foreach (var runId in await _commits.ListRunIdsAsync(cancellationToken).ConfigureAwait(false))
        {
            var initial = await _commits.LoadCommitAsync(runId, 1, cancellationToken).ConfigureAwait(false);
            if (initial?.Lineage == null)
                continue;
            var validation = initial.Lineage.Validate(runId);
            if (validation.IsFailure)
                throw new InvalidDataException($"Invalid lineage for run {runId}: {validation.Error}");
            rebuilt[runId] = new RunLineageEntry(runId, initial.Lineage);
        }
        ValidateGraph(rebuilt);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _entries = rebuilt.ToImmutable();
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;
        await RebuildAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateGraph(IReadOnlyDictionary<Guid, RunLineageEntry> entries)
    {
        foreach (var entry in entries.Values)
        {
            var lineage = entry.Lineage;
            if (lineage.ParentRunId == null)
                continue;
            if (!entries.TryGetValue(lineage.RootRunId, out var root) || root.Lineage.ParentRunId != null)
                throw new InvalidDataException($"Lineage root is unavailable for run {entry.RunId}");

            var visited = new HashSet<Guid> { entry.RunId };
            var current = entry;
            while (current.Lineage.ParentRunId is { } parentId)
            {
                if (!visited.Add(parentId))
                    throw new InvalidDataException($"Lineage cycle detected at run {entry.RunId}");
                if (!entries.TryGetValue(parentId, out var parent))
                    throw new InvalidDataException($"Lineage parent {parentId} is unavailable for run {entry.RunId}");
                current = parent;
                if (current.Lineage.RootRunId != lineage.RootRunId)
                    throw new InvalidDataException($"Lineage root mismatch for run {entry.RunId}");
            }
            if (current.RunId != lineage.RootRunId)
                throw new InvalidDataException($"Lineage chain does not terminate at root for run {entry.RunId}");
        }
    }
}
