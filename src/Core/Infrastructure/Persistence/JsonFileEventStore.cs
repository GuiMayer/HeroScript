using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Abstractions.Persistence;
using Core.Events;
using Core.Logging;

namespace Core.Infrastructure.Persistence;

/// <summary>
/// Appends events to a JSON Lines file (one JSON object per line).
/// Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class JsonFileEventStore : IEventStore, IDisposable
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly JsonSerializerOptions _writeOptions;
    private readonly JsonSerializerOptions _readOptions;

    public JsonFileEventStore(string storePath, ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(storePath);
        _filePath = Path.Combine(storePath, "events.jsonl");

        _writeOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _readOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc />
    public async Task AppendAsync(IEvent @event, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Wrap in an envelope that preserves the event type for deserialization
            var envelope = new EventEnvelope
            {
                EventType = @event.EventType,
                EventId = @event.EventId,
                Timestamp = @event.Timestamp,
                Payload = JsonSerializer.SerializeToElement(@event, @event.GetType(), _writeOptions)
            };

            var line = JsonSerializer.Serialize(envelope, _writeOptions);

            await using var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to append event {{{@event.EventType}}} to store: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IEvent>> GetEventsAsync(EventStoreFilter filter, CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<IEvent>();

        var results = new List<IEvent>();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope>(line, _readOptions);
                    if (envelope == null) continue;

                    // Deserialize as GameEvent (the concrete base for all domain events)
                    var gameEvent = envelope.Payload.Deserialize<GameEvent>(_readOptions);
                    if (gameEvent == null) continue;

                    // Apply filters
                    if (filter.AfterSequence >= 0 && gameEvent.Sequence <= filter.AfterSequence)
                        continue;
                    if (filter.EventType != null &&
                        !string.Equals(gameEvent.EventType, filter.EventType, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (filter.RunId.HasValue &&
                        !gameEvent.Payload.TryGetValue("runId", out var runIdObj) &&
                        runIdObj?.ToString() != filter.RunId.Value.ToString())
                        continue;
                    if (filter.CombatId.HasValue &&
                        !gameEvent.Payload.TryGetValue("combatId", out var combatIdObj) &&
                        combatIdObj?.ToString() != filter.CombatId.Value.ToString())
                        continue;

                    results.Add(gameEvent);

                    if (filter.Limit > 0 && results.Count >= filter.Limit)
                        break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to deserialize event line: {ex.Message}", ex);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }

        return results;
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class EventEnvelope
    {
        public string EventType { get; set; } = string.Empty;
        public Guid EventId { get; set; }
        public DateTime Timestamp { get; set; }
        public JsonElement Payload { get; set; }
    }
}
