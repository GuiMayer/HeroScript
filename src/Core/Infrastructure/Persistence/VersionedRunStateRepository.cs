using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Logging;
using Core.Run;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Persists an append-only checkpoint journal and a compactable snapshot
/// projection. Journal retention is independent from snapshot retention.
/// Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class VersionedRunStateRepository : IRunCheckpointRepository, IDisposable
{
    private readonly string _storePath;
    private readonly ILogger _logger;
    private readonly int _maxRetainedSnapshots;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly JsonSerializerOptions _options;

    public VersionedRunStateRepository(string storePath, ILogger logger, int maxRetainedSnapshots = 100)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storePath = storePath;
        _maxRetainedSnapshots = maxRetainedSnapshots;
        Directory.CreateDirectory(storePath);

        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    /// <inheritdoc />
    public async Task SaveAsync(RunState state, CancellationToken ct = default)
    {
        var entry = new RunJournalEntry
        {
            RunId = state.RunId,
            Sequence = state.Sequence,
            Step = state.Determinism.Step,
            CommandType = "legacy.snapshot",
            Command = JsonSerializer.SerializeToElement(new { }),
            StateHash = CanonicalJson.ComputeHash(state),
            LogicalTimestamp = state.Determinism.LogicalTimestamp.UtcDateTime
        };
        await SaveCheckpointAsync(new RunCheckpoint(state, entry), ct).ConfigureAwait(false);
    }

    public Task SaveCheckpointAsync(RunCheckpoint checkpoint, CancellationToken ct = default) =>
        SaveCheckpointBatchAsync(RunCheckpointBatch.Single(checkpoint), ct);

    public async Task SaveCheckpointBatchAsync(RunCheckpointBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.Validate();
        var first = batch.Checkpoints[0];
        var final = batch.Checkpoints[^1];
        var state = final.State;
        
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshotDir = GetSnapshotDirectory(state.RunId);
            var journalDir = GetJournalDirectory(state.RunId);
            var batchDir = GetBatchDirectory(state.RunId);
            Directory.CreateDirectory(snapshotDir);
            Directory.CreateDirectory(journalDir);
            Directory.CreateDirectory(batchDir);
            await MigrateSnapshotsToJournalAsync(state.RunId, ct).ConfigureAwait(false);

            var batchJson = JsonSerializer.Serialize(batch, _options);
            var batchPath = GetBatchPath(state.RunId, first.State.Sequence, final.State.Sequence);
            var batchTmp = batchPath + ".tmp";
            await File.WriteAllTextAsync(batchTmp, batchJson, ct).ConfigureAwait(false);
            File.Move(batchTmp, batchPath, overwrite: false);

            // Snapshots are a rebuildable read optimization. Once the journal
            // transaction is durable, a projection failure must not roll back an
            // accepted command.
            try
            {
                foreach (var checkpoint in batch.Checkpoints)
                {
                    var snapshotPath = GetSnapshotPath(state.RunId, checkpoint.State.Sequence);
                    var snapshotTmp = snapshotPath + ".tmp";
                    var snapshotJson = JsonSerializer.Serialize(checkpoint, _options);
                    await File.WriteAllTextAsync(snapshotTmp, snapshotJson, ct).ConfigureAwait(false);
                    File.Move(snapshotTmp, snapshotPath, overwrite: false);
                }
            }
            catch (Exception projectionException)
            {
                CleanupTempFile(GetSnapshotPath(state.RunId, state.Sequence) + ".tmp");
                _logger.LogWarning(
                    $"Checkpoint {state.Sequence} is durable in the journal, but its snapshot projection failed: " +
                    projectionException.Message);
            }

            _logger.LogInformation(
                $"Journal batch {first.State.Sequence}-{final.State.Sequence} saved for run {state.RunId}");

            // Cleanup old snapshots if exceeding max retention
            await CleanupOldSnapshotsAsync(state.RunId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to save journal batch for run {state.RunId}: {ex.Message}", ex);
            CleanupTempFile(GetBatchPath(state.RunId, first.State.Sequence, final.State.Sequence) + ".tmp");
            foreach (var checkpoint in batch.Checkpoints)
                CleanupTempFile(GetSnapshotPath(state.RunId, checkpoint.State.Sequence) + ".tmp");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RunState?> LoadLatestAsync(Guid runId, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        int? latestSequence;
        try
        {
            var sequences = ListSnapshotSequences(GetSnapshotDirectory(runId))
                .Concat(ListSnapshotSequences(GetJournalDirectory(runId)))
                .Concat(ListBatchSequences(GetBatchDirectory(runId)))
                .Distinct()
                .ToArray();
            latestSequence = sequences.Length == 0 ? null : sequences.Max();
        }
        finally
        {
            _semaphore.Release();
        }

        if (!latestSequence.HasValue)
            return null;

        return await LoadAsync(runId, latestSequence.Value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RunState?> LoadAsync(Guid runId, int sequence, CancellationToken ct = default)
    {
        var snapshotPath = GetSnapshotPath(runId, sequence);
        var journalPath = GetJournalPath(runId, sequence);

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RunState? state;
            if (File.Exists(snapshotPath) || File.Exists(journalPath))
            {
                var path = File.Exists(snapshotPath) ? snapshotPath : journalPath;
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                state = DeserializeCheckpoint(json)?.State;
            }
            else
            {
                state = (await LoadBatchesUnsafeAsync(runId, ct).ConfigureAwait(false))
                    .SelectMany(batch => batch.Checkpoints)
                    .FirstOrDefault(checkpoint => checkpoint.State.Sequence == sequence)?.State;
            }
            
            _logger.LogInformation($"Snapshot {sequence} loaded for run {runId}");
            
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load snapshot {sequence} for run {runId}: {ex.Message}", ex);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> ListSnapshotsAsync(Guid runId, CancellationToken ct = default)
    {
        var snapshotDir = GetSnapshotDirectory(runId);
        if (!Directory.Exists(snapshotDir))
            return Array.Empty<int>();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return ListSnapshotSequences(snapshotDir);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<RunCheckpoint>> LoadCheckpointsAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var checkpoints = new Dictionary<int, RunCheckpoint>();
            var journalDir = GetJournalDirectory(runId);
            var sourceDir = Directory.Exists(journalDir) && ListSnapshotSequences(journalDir).Count > 0
                ? journalDir
                : GetSnapshotDirectory(runId);
            foreach (var sequence in ListSnapshotSequences(sourceDir))
            {
                var json = await File.ReadAllTextAsync(
                    Path.Combine(sourceDir, $"{sequence:D6}.json"),
                    ct).ConfigureAwait(false);
                var checkpoint = DeserializeCheckpoint(json);
                if (checkpoint != null)
                    checkpoints[sequence] = checkpoint;
            }
            foreach (var batch in await LoadBatchesUnsafeAsync(runId, ct).ConfigureAwait(false))
            foreach (var checkpoint in batch.Checkpoints)
                checkpoints[checkpoint.State.Sequence] = checkpoint;
            return checkpoints.Values.OrderBy(checkpoint => checkpoint.State.Sequence).ToArray();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<IReadOnlyList<RunJournalEntry>> LoadJournalAsync(
        Guid runId,
        int afterSequence = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1000");

        var checkpoints = await LoadCheckpointsAsync(runId, ct).ConfigureAwait(false);
        return checkpoints
            .Where(item => item.JournalEntry.Sequence > afterSequence)
            .OrderBy(item => item.JournalEntry.Sequence)
            .Take(limit)
            .Select(item => item.JournalEntry)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid runId, CancellationToken ct = default)
    {
        var runDir = GetRunDirectory(runId);
        
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(runDir))
            {
                Directory.Delete(runDir, recursive: true);
                _logger.LogInformation($"All snapshots deleted for run {runId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete run {runId}: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListRunIdsAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_storePath))
                return Array.Empty<Guid>();

            return Directory.GetDirectories(_storePath)
                .Select(d => Path.GetFileName(d))
                .Where(name => Guid.TryParse(name, out _))
                .Select(name => Guid.Parse(name))
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();

    private string GetRunDirectory(Guid runId) => Path.Combine(_storePath, runId.ToString());
    private string GetSnapshotDirectory(Guid runId) => Path.Combine(GetRunDirectory(runId), "snapshots");
    private string GetJournalDirectory(Guid runId) => Path.Combine(GetRunDirectory(runId), "journal");
    private string GetBatchDirectory(Guid runId) => Path.Combine(GetRunDirectory(runId), "batches");
    private string GetSnapshotPath(Guid runId, int sequence) => 
        Path.Combine(GetSnapshotDirectory(runId), $"{sequence:D6}.json");
    private string GetJournalPath(Guid runId, int sequence) =>
        Path.Combine(GetJournalDirectory(runId), $"{sequence:D6}.json");
    private string GetBatchPath(Guid runId, int firstSequence, int lastSequence) =>
        Path.Combine(GetBatchDirectory(runId), $"{firstSequence:D6}-{lastSequence:D6}.json");

    private async Task<IReadOnlyList<RunCheckpointBatch>> LoadBatchesUnsafeAsync(
        Guid runId,
        CancellationToken ct)
    {
        var directory = GetBatchDirectory(runId);
        if (!Directory.Exists(directory))
            return [];
        var batches = new List<RunCheckpointBatch>();
        foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var batch = JsonSerializer.Deserialize<RunCheckpointBatch>(json, _options);
            if (batch != null)
                batches.Add(batch);
        }
        return batches;
    }

    private static IReadOnlyList<int> ListBatchSequences(string batchDir)
    {
        if (!Directory.Exists(batchDir))
            return [];
        return Directory.GetFiles(batchDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .SelectMany(name => name?.Split('-', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where(value => int.TryParse(value, out _))
            .Select(int.Parse)
            .ToArray();
    }

    private async Task MigrateSnapshotsToJournalAsync(Guid runId, CancellationToken ct)
    {
        var snapshotDir = GetSnapshotDirectory(runId);
        foreach (var sequence in ListSnapshotSequences(snapshotDir))
        {
            var target = GetJournalPath(runId, sequence);
            if (File.Exists(target))
                continue;

            var json = await File.ReadAllTextAsync(GetSnapshotPath(runId, sequence), ct).ConfigureAwait(false);
            var tmp = target + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: false);
        }
    }

    private static void CleanupTempFile(string path)
    {
        if (!File.Exists(path))
            return;
        try { File.Delete(path); } catch { /* best effort */ }
    }

    private async Task CleanupOldSnapshotsAsync(Guid runId, CancellationToken ct)
    {
        if (_maxRetainedSnapshots <= 0)
            return;

        // SaveAsync already holds _semaphore. Calling the public ListSnapshotsAsync
        // here would attempt to acquire it again and deadlock persistence.
        var snapshots = ListSnapshotSequences(GetSnapshotDirectory(runId));
        if (snapshots.Count <= _maxRetainedSnapshots)
            return;

        var toDelete = snapshots.Take(snapshots.Count - _maxRetainedSnapshots).ToList();
        foreach (var sequence in toDelete)
        {
            try
            {
                var path = GetSnapshotPath(runId, sequence);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    _logger.LogDebug($"Deleted old snapshot {sequence} for run {runId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to delete old snapshot {sequence} for run {runId}: {ex.Message}");
            }
        }
    }

    private static IReadOnlyList<int> ListSnapshotSequences(string snapshotDir)
    {
        if (!Directory.Exists(snapshotDir))
            return Array.Empty<int>();

        return Directory.GetFiles(snapshotDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => int.TryParse(name, out _))
            .Select(name => int.Parse(name!))
            .OrderBy(sequence => sequence)
            .ToList();
    }

    private RunCheckpoint? DeserializeCheckpoint(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("state", out _)
            || document.RootElement.TryGetProperty("State", out _))
        {
            return JsonSerializer.Deserialize<RunCheckpoint>(json, _options);
        }

        var state = JsonSerializer.Deserialize<RunState>(json, _options);
        if (state == null)
            return null;
        return new RunCheckpoint(state, new RunJournalEntry
        {
            RunId = state.RunId,
            Sequence = state.Sequence,
            Step = state.Determinism.Step,
            CommandType = "legacy.snapshot",
            Command = JsonSerializer.SerializeToElement(new { }),
            StateHash = CanonicalJson.ComputeHash(state),
            LogicalTimestamp = state.Determinism.LogicalTimestamp.UtcDateTime
        });
    }
}
