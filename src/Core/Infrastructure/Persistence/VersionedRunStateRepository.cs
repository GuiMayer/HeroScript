using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Logging;
using Core.Run;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Persists each RunState as versioned snapshots inside {runId}/snapshots/ directory.
/// Snapshots are immutable and append-only. Format: {sequence:D6}.json (e.g., 000001.json, 000042.json)
/// Uses atomic write (temp file + rename) to prevent corruption on crash.
/// Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class VersionedRunStateRepository : IRunStateRepository, IDisposable
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
        ArgumentNullException.ThrowIfNull(state);
        
        var snapshotDir = GetSnapshotDirectory(state.RunId);
        Directory.CreateDirectory(snapshotDir);

        var path = GetSnapshotPath(state.RunId, state.Sequence);
        var tmp = path + ".tmp";

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(state, _options);
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: false); // Never overwrite existing snapshots

            _logger.LogInformation("Snapshot {Sequence} saved for run {RunId}", state.Sequence, state.RunId);

            // Cleanup old snapshots if exceeding max retention
            await CleanupOldSnapshotsAsync(state.RunId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to save snapshot {Sequence} for run {RunId}: {Message}", state.Sequence, state.RunId, ex.Message, ex);
            // Clean up temp file if it exists
            if (File.Exists(tmp))
                try { File.Delete(tmp); } catch { /* best effort */ }
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
        var snapshots = await ListSnapshotsAsync(runId, ct).ConfigureAwait(false);
        if (snapshots.Count == 0)
            return null;

        var latestSequence = snapshots.Max();
        return await LoadAsync(runId, latestSequence, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RunState?> LoadAsync(Guid runId, int sequence, CancellationToken ct = default)
    {
        var path = GetSnapshotPath(runId, sequence);
        if (!File.Exists(path))
            return null;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<RunState>(json, _options);
            
            _logger.LogInformation("Snapshot {Sequence} loaded for run {RunId}", sequence, runId);
            
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to load snapshot {Sequence} for run {RunId}: {Message}", sequence, runId, ex.Message, ex);
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
            return Directory.GetFiles(snapshotDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(name => int.TryParse(name, out _))
                .Select(name => int.Parse(name))
                .OrderBy(seq => seq)
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
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
                _logger.LogInformation("All snapshots deleted for run {RunId}", runId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to delete run {RunId}: {Message}", runId, ex.Message, ex);
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
    private string GetSnapshotPath(Guid runId, int sequence) => 
        Path.Combine(GetSnapshotDirectory(runId), $"{sequence:D6}.json");

    private async Task CleanupOldSnapshotsAsync(Guid runId, CancellationToken ct)
    {
        if (_maxRetainedSnapshots <= 0)
            return;

        var snapshots = await ListSnapshotsAsync(runId, ct).ConfigureAwait(false);
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
                    _logger.LogDebug("Deleted old snapshot {Sequence} for run {RunId}", sequence, runId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to delete old snapshot {Sequence} for run {RunId}: {Message}", 
                    sequence, runId, ex.Message);
            }
        }
    }
}
