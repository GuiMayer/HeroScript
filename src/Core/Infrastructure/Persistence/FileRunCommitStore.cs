using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Logging;
using Core.Run;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Append-only file store. Each command commit is flushed to a temporary file
/// and atomically renamed to its final immutable sequence path.
/// </summary>
public sealed class FileRunCommitStore : IRunCommitStore, IDisposable
{
    private readonly string _storePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly JsonSerializerOptions _options;

    public FileRunCommitStore(string storePath, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storePath = Path.GetFullPath(storePath);
        Directory.CreateDirectory(_storePath);
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<RunCommitAppendResult> AppendAsync(
        RunCommit commit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        commit.Validate();
        var path = GetCommitPath(commit.RunId, commit.Sequence);
        var temporaryPath = path + ".tmp";

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(GetCommitDirectory(commit.RunId));
            var previous = commit.Sequence == 1
                ? null
                : await LoadCommitUnsafeAsync(commit.RunId, commit.Sequence - 1, ct).ConfigureAwait(false);
            if (commit.Sequence > 1 && previous == null)
                throw new InvalidOperationException($"Previous run commit is missing: {commit.Sequence - 1}");
            if (previous != null && !string.Equals(
                    previous.StateHash,
                    commit.PreviousStateHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Run commit hash chain is not contiguous");

            if (File.Exists(path))
            {
                var existing = await LoadCommitUnsafeAsync(commit.RunId, commit.Sequence, ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Existing run commit is unreadable: {path}");
                if (!string.Equals(
                        CanonicalJson.ComputeHash(existing),
                        CanonicalJson.ComputeHash(commit),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Run commit sequence collision: {commit.RunId}/{commit.Sequence}");
                return new RunCommitAppendResult(existing, true);
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(commit, _options);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: false);
            _logger.LogInformation($"Run commit {commit.Sequence} appended for run {commit.RunId}");
            return new RunCommitAppendResult(commit, false);
        }
        catch
        {
            CleanupTempFile(temporaryPath);
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<RunCommit?> LoadCommitAsync(
        Guid runId,
        int sequence,
        CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadCommitUnsafeAsync(runId, sequence, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<RunCommit>> LoadCommitsAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sequences = ListSequencesUnsafe(runId);
            var commits = new List<RunCommit>(sequences.Count);
            RunCommit? previous = null;
            foreach (var sequence in sequences)
            {
                var commit = await LoadCommitUnsafeAsync(runId, sequence, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Run commit disappeared while reading: {runId}/{sequence}");
                if (previous != null && !string.Equals(
                        previous.StateHash,
                        commit.PreviousStateHash,
                        StringComparison.Ordinal))
                    throw new InvalidDataException($"Broken run commit hash chain at sequence {sequence}");
                commits.Add(commit);
                previous = commit;
            }
            return commits;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<RunState?> LoadStateAsync(
        Guid runId,
        int sequence,
        CancellationToken ct = default) =>
        (await LoadCommitAsync(runId, sequence, ct).ConfigureAwait(false))?.StateAfter;

    public async Task<RunState?> LoadLatestStateAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sequence = ListSequencesUnsafe(runId).LastOrDefault();
            return sequence == 0
                ? null
                : (await LoadCommitUnsafeAsync(runId, sequence, ct).ConfigureAwait(false))?.StateAfter;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<int>> ListCommitSequencesAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return ListSequencesUnsafe(runId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_storePath))
                return [];
            return Directory.GetDirectories(_storePath)
                .Select(Path.GetFileName)
                .Where(name => Guid.TryParse(name, out _))
                .Select(name => Guid.Parse(name!))
                .OrderBy(id => id)
                .ToArray();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task DeleteRunAsync(Guid runId, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = GetRunDirectory(runId);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();

    private async Task<RunCommit?> LoadCommitUnsafeAsync(
        Guid runId,
        int sequence,
        CancellationToken ct)
    {
        var path = GetCommitPath(runId, sequence);
        if (!File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var commit = JsonSerializer.Deserialize<RunCommit>(json, _options)
            ?? throw new InvalidDataException($"Run commit is empty: {path}");
        commit.Validate();
        return commit;
    }

    private IReadOnlyList<int> ListSequencesUnsafe(Guid runId)
    {
        var path = GetCommitDirectory(runId);
        if (!Directory.Exists(path))
            return [];
        return Directory.GetFiles(path, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => int.TryParse(name, out _))
            .Select(name => int.Parse(name!))
            .OrderBy(sequence => sequence)
            .ToArray();
    }

    private string GetRunDirectory(Guid runId) => Path.Combine(_storePath, runId.ToString("D"));
    private string GetCommitDirectory(Guid runId) => Path.Combine(GetRunDirectory(runId), "commits");
    private string GetCommitPath(Guid runId, int sequence) =>
        Path.Combine(GetCommitDirectory(runId), $"{sequence:D8}.json");

    private static void CleanupTempFile(string path)
    {
        if (!File.Exists(path))
            return;
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
