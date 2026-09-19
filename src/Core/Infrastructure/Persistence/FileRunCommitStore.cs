using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Caching;
using Core.Logging;
using Core.Run;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Append-only file store. Commands from the same run are serialized while
/// unrelated runs can persist concurrently. Each append is flushed to a
/// temporary file and atomically renamed to its immutable sequence path.
/// </summary>
public sealed class FileRunCommitStore : IRunCommitStore, IPreparedRunCommitStore, IDisposable
{
    private readonly string _storePath;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _runGates = new();
    private readonly ConcurrentDictionary<Guid, AppendCursor> _appendCursors = new();
    private readonly LruCache<string, ValidatedCommitIdentity>? _validatedBytes;

    private sealed record AppendCursor(int Sequence, string StateHash, string ByteHash);

    private sealed record ValidatedCommitIdentity(
        Guid RunId, int Sequence, Guid CommandId, string StateHash, string PreviousStateHash)
    {
        public static ValidatedCommitIdentity From(RunCommit commit) => new(
            commit.RunId,
            commit.Sequence,
            commit.RootCommand.CommandId,
            commit.StateHash,
            commit.PreviousStateHash);
    }

    public FileRunCommitStore(string storePath, ILogger logger, int validationCacheCapacity = 256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentOutOfRangeException.ThrowIfNegative(validationCacheCapacity);
        _validatedBytes = validationCacheCapacity == 0 ? null : new(validationCacheCapacity);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storePath = Path.GetFullPath(storePath);
        Directory.CreateDirectory(_storePath);
    }

    public Task<RunCommitAppendResult> AppendAsync(
        RunCommit commit,
        CancellationToken ct = default) =>
        AppendPreparedAsync(PreparedRunCommit.Create(commit), ct);

    public async Task<RunCommitAppendResult> AppendPreparedAsync(
        PreparedRunCommit prepared,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var commit = prepared.Commit;
        var path = GetCommitPath(commit.RunId, commit.Sequence);
        var temporaryPath = path + ".tmp";
        var gate = Gate(commit.RunId);

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(GetCommitDirectory(commit.RunId));
            var cursor = await GetAppendCursorUnsafeAsync(commit.RunId, ct).ConfigureAwait(false);

            if (File.Exists(path))
            {
                var existingBytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
                var existingByteHash = Convert.ToHexString(SHA256.HashData(existingBytes));
                var existing = DeserializeValidated(existingBytes, existingByteHash);
                if (!string.Equals(existingByteHash, prepared.ByteHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Run commit sequence collision: {commit.RunId}/{commit.Sequence}");
                }
                await WriteCommandIndexBestEffortAsync(existing, ct).ConfigureAwait(false);
                return new RunCommitAppendResult(existing, true);
            }

            var expectedSequence = checked(cursor.Sequence + 1);
            if (commit.Sequence != expectedSequence)
            {
                throw new InvalidOperationException(
                    $"Run commit sequence is not contiguous: expected {expectedSequence}, got {commit.Sequence}");
            }
            if (commit.Sequence == 1 && !string.IsNullOrWhiteSpace(commit.PreviousStateHash))
                throw new InvalidOperationException("Initial run commit cannot reference a previous state");
            if (commit.Sequence > 1 && !File.Exists(GetCommitPath(commit.RunId, commit.Sequence - 1)))
                throw new InvalidOperationException($"Previous run commit is missing: {commit.Sequence - 1}");
            if (commit.Sequence > 1 && !string.Equals(
                    cursor.StateHash,
                    commit.PreviousStateHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Run commit hash chain is not contiguous");
            }

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(prepared.Bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: false);
            _appendCursors[commit.RunId] = new AppendCursor(
                commit.Sequence,
                commit.StateHash,
                prepared.ByteHash);
            _validatedBytes?.Set(
                prepared.ByteHash,
                ValidatedCommitIdentity.From(commit));
            await WriteCommandIndexBestEffortAsync(commit, ct).ConfigureAwait(false);
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
            gate.Release();
        }
    }

    public async Task<RunCommit?> LoadCommitAsync(
        Guid runId,
        int sequence,
        CancellationToken ct = default)
    {
        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadCommitUnsafeAsync(runId, sequence, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RunCommit>> LoadCommitsAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
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
                {
                    throw new InvalidDataException($"Broken run commit hash chain at sequence {sequence}");
                }
                commits.Add(commit);
                previous = commit;
            }
            return commits;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RunCommit>> LoadCommitsAsync(
        Guid runId,
        int afterSequence,
        int limit,
        CancellationToken ct = default)
    {
        if (afterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sequences = ListSequencesUnsafe(runId)
                .Where(sequence => sequence > afterSequence)
                .Take(limit)
                .ToArray();
            if (sequences.Length == 0)
                return [];

            RunCommit? previous = sequences[0] > 1
                ? await LoadCommitUnsafeAsync(runId, sequences[0] - 1, ct).ConfigureAwait(false)
                : null;
            var commits = new List<RunCommit>(sequences.Length);
            foreach (var sequence in sequences)
            {
                var commit = await LoadCommitUnsafeAsync(runId, sequence, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Run commit disappeared while reading: {runId}/{sequence}");
                if (previous != null && !string.Equals(
                        previous.StateHash,
                        commit.PreviousStateHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Broken run commit hash chain at sequence {sequence}");
                }
                commits.Add(commit);
                previous = commit;
            }
            return commits;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RunCommit?> FindCommandAsync(
        Guid runId,
        Guid commandId,
        CancellationToken ct = default)
    {
        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var indexedSequence = await ReadCommandIndexUnsafeAsync(runId, commandId, ct)
                .ConfigureAwait(false);
            if (indexedSequence.HasValue)
            {
                var indexed = await LoadCommitUnsafeAsync(runId, indexedSequence.Value, ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidDataException(
                        $"Indexed run commit is missing: {runId}/{indexedSequence.Value}");
                if (indexed.RootCommand.CommandId != commandId)
                    throw new InvalidDataException($"Run command index is invalid: {runId}/{commandId}");
                return indexed;
            }

            var sequences = ListSequencesUnsafe(runId);
            var latestSequence = sequences.LastOrDefault();
            if (await IsCommandIndexCompleteUnsafeAsync(runId, latestSequence, ct).ConfigureAwait(false))
                return null;

            ValidatedCommitIdentity? previous = null;
            int? found = null;
            var indexComplete = true;
            foreach (var sequence in sequences)
            {
                var bytes = await File.ReadAllBytesAsync(GetCommitPath(runId, sequence), ct).ConfigureAwait(false);
                var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                ValidatedCommitIdentity? identity = null;
                if (_validatedBytes == null || !_validatedBytes.TryGetValue(fingerprint, out identity))
                {
                    var commit = DeserializeValidated(bytes, fingerprint);
                    identity = ValidatedCommitIdentity.From(commit);
                }
                if (identity!.RunId != runId || identity.Sequence != sequence)
                    throw new InvalidDataException($"Run commit path does not match its identity: {runId}/{sequence}");
                if (previous != null && !string.Equals(
                        previous.StateHash,
                        identity.PreviousStateHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Broken run commit hash chain at sequence {sequence}");
                }
                if (identity.CommandId == commandId)
                    found = sequence;
                indexComplete &= await WriteCommandIndexEntryBestEffortAsync(
                    runId,
                    identity.CommandId,
                    identity.Sequence,
                    ct).ConfigureAwait(false);
                previous = identity;
            }
            if (indexComplete)
                await WriteCommandIndexHeadBestEffortAsync(runId, latestSequence, ct).ConfigureAwait(false);
            return found.HasValue
                ? await LoadCommitUnsafeAsync(runId, found.Value, ct).ConfigureAwait(false)
                : null;
        }
        finally
        {
            gate.Release();
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
        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sequence = ListSequencesUnsafe(runId).LastOrDefault();
            return sequence == 0
                ? null
                : (await LoadCommitUnsafeAsync(runId, sequence, ct).ConfigureAwait(false))?.StateAfter;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<int>> ListCommitSequencesAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return ListSequencesUnsafe(runId);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(_storePath))
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        IReadOnlyList<Guid> ids = Directory.GetDirectories(_storePath)
            .Select(Path.GetFileName)
            .Where(name => Guid.TryParse(name, out _))
            .Select(name => Guid.Parse(name!))
            .OrderBy(id => id)
            .ToArray();
        return Task.FromResult(ids);
    }

    public async Task DeleteRunAsync(Guid runId, CancellationToken ct = default)
    {
        var gate = Gate(runId);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = GetRunDirectory(runId);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            _appendCursors.TryRemove(runId, out _);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        _validatedBytes?.Clear();
        _appendCursors.Clear();
        foreach (var gate in _runGates.Values)
            gate.Dispose();
        _runGates.Clear();
    }

    private SemaphoreSlim Gate(Guid runId) =>
        _runGates.GetOrAdd(runId, static _ => new SemaphoreSlim(1, 1));

    private async Task<AppendCursor> GetAppendCursorUnsafeAsync(Guid runId, CancellationToken ct)
    {
        if (_appendCursors.TryGetValue(runId, out var cursor))
            return cursor;

        var sequence = ListSequencesUnsafe(runId).LastOrDefault();
        if (sequence == 0)
        {
            cursor = new AppendCursor(0, string.Empty, string.Empty);
        }
        else
        {
            var path = GetCommitPath(runId, sequence);
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var byteHash = Convert.ToHexString(SHA256.HashData(bytes));
            var commit = DeserializeValidated(bytes, byteHash);
            cursor = new AppendCursor(commit.Sequence, commit.StateHash, byteHash);
        }

        _appendCursors[runId] = cursor;
        return cursor;
    }

    private async Task<RunCommit?> LoadCommitUnsafeAsync(
        Guid runId,
        int sequence,
        CancellationToken ct)
    {
        var path = GetCommitPath(runId, sequence);
        if (!File.Exists(path))
            return null;
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var fingerprint = _validatedBytes == null
            ? null
            : Convert.ToHexString(SHA256.HashData(bytes));
        return DeserializeValidated(bytes, fingerprint);
    }

    private RunCommit DeserializeValidated(byte[] bytes, string? fingerprint)
    {
        // Cache only validation of exact bytes, never deserialized objects.
        // Changed bytes cannot reuse validation, even if file metadata matches.
        var commit = JsonSerializer.Deserialize<RunCommit>(bytes, RunCommitJson.Options)
            ?? throw new InvalidDataException("Run commit is empty");
        if (_validatedBytes == null || fingerprint == null || !_validatedBytes.TryGetValue(fingerprint, out _))
        {
            commit.Validate();
            if (fingerprint != null)
                _validatedBytes?.Set(fingerprint, ValidatedCommitIdentity.From(commit));
        }
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
    private string GetCommandIndexDirectory(Guid runId) =>
        Path.Combine(GetRunDirectory(runId), "index", "commands");
    private string GetCommandIndexPath(Guid runId, Guid commandId) =>
        Path.Combine(GetCommandIndexDirectory(runId), $"{commandId:N}.idx");
    private string GetCommandIndexHeadPath(Guid runId) =>
        Path.Combine(GetRunDirectory(runId), "index", "head.idx");
    private string GetCommitPath(Guid runId, int sequence) =>
        Path.Combine(GetCommitDirectory(runId), $"{sequence:D8}.json");

    private static void CleanupTempFile(string path)
    {
        if (!File.Exists(path))
            return;
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private async Task<int?> ReadCommandIndexUnsafeAsync(
        Guid runId,
        Guid commandId,
        CancellationToken ct)
    {
        var path = GetCommandIndexPath(runId, commandId);
        if (!File.Exists(path))
            return null;
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return int.TryParse(text, out var sequence) && sequence > 0
            ? sequence
            : throw new InvalidDataException($"Run command index is unreadable: {runId}/{commandId}");
    }

    private async Task<bool> IsCommandIndexCompleteUnsafeAsync(
        Guid runId,
        int latestSequence,
        CancellationToken ct)
    {
        if (latestSequence == 0)
            return true;
        var path = GetCommandIndexHeadPath(runId);
        if (!File.Exists(path))
            return false;
        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return int.TryParse(text, out var indexedSequence) && indexedSequence == latestSequence;
    }

    private async Task WriteCommandIndexBestEffortAsync(RunCommit commit, CancellationToken ct)
    {
        var entryWritten = await WriteCommandIndexEntryBestEffortAsync(
            commit.RunId,
            commit.RootCommand.CommandId,
            commit.Sequence,
            ct).ConfigureAwait(false);
        if (entryWritten)
            await WriteCommandIndexHeadBestEffortAsync(commit.RunId, commit.Sequence, ct)
                .ConfigureAwait(false);
    }

    private async Task<bool> WriteCommandIndexEntryBestEffortAsync(
        Guid runId,
        Guid commandId,
        int sequence,
        CancellationToken ct) =>
        await WriteDerivedIndexBestEffortAsync(
            GetCommandIndexPath(runId, commandId),
            sequence,
            ct).ConfigureAwait(false);

    private async Task<bool> WriteCommandIndexHeadBestEffortAsync(
        Guid runId,
        int sequence,
        CancellationToken ct) =>
        await WriteDerivedIndexBestEffortAsync(
            GetCommandIndexHeadPath(runId),
            sequence,
            ct).ConfigureAwait(false);

    private async Task<bool> WriteDerivedIndexBestEffortAsync(
        string path,
        int sequence,
        CancellationToken ct)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporaryPath, sequence.ToString(), ct).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CleanupTempFile(temporaryPath);
            throw;
        }
        catch (Exception exception)
        {
            CleanupTempFile(temporaryPath);
            _logger.LogWarning($"Failed to update derived run index '{path}': {exception.Message}");
            return false;
        }
    }
}
