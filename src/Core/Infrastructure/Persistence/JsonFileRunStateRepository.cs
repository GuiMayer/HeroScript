using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Logging;
using Core.Run;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Persists each RunState as {runId}.json inside the configured directory.
/// Uses atomic write (temp file + rename) to prevent corruption on crash.
/// Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class JsonFileRunStateRepository : IRunCheckpointRepository, IDisposable
{
    private readonly string _storePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly JsonSerializerOptions _options;

    public JsonFileRunStateRepository(string storePath, ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _storePath = storePath;
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

    public async Task SaveCheckpointAsync(RunCheckpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        var state = checkpoint.State;
        ArgumentNullException.ThrowIfNull(state);
        var path = GetPath(state.RunId);
        var tmp = path + ".tmp";

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(checkpoint, _options);
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to save run {state.RunId}: {ex.Message}", ex);
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
        var path = GetPath(runId);
        if (!File.Exists(path))
            return null;

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return DeserializeCheckpoint(json)?.State;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load run {runId}: {ex.Message}", ex);
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RunState?> LoadAsync(Guid runId, int sequence, CancellationToken ct = default)
    {
        // This simple implementation doesn't support versioned snapshots
        // Always returns the latest (and only) snapshot, ignoring sequence parameter
        return await LoadLatestAsync(runId, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> ListSnapshotsAsync(Guid runId, CancellationToken ct = default)
    {
        // This simple implementation doesn't support versioned snapshots
        // Returns a single entry if the file exists
        var state = await LoadLatestAsync(runId, ct).ConfigureAwait(false);
        if (state == null)
            return Array.Empty<int>();

        return new[] { state.Sequence };
    }

    public async Task<IReadOnlyList<RunCheckpoint>> LoadCheckpointsAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        var path = GetPath(runId);
        if (!File.Exists(path))
            return Array.Empty<RunCheckpoint>();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var checkpoint = DeserializeCheckpoint(json);
            return checkpoint == null ? Array.Empty<RunCheckpoint>() : new[] { checkpoint };
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid runId, CancellationToken ct = default)
    {
        var path = GetPath(runId);
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
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
            return Directory.GetFiles(_storePath, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
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

    private string GetPath(Guid runId) => Path.Combine(_storePath, $"{runId}.json");

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
