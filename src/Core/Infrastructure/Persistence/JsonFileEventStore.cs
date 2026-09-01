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
                Sequence = @event is GameEvent gameEvent ? gameEvent.Sequence : null,
                Context = @event is GameEvent contextualEvent
                    ? contextualEvent.Context
                    : GameEventContext.Empty,
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
            await writer.FlushAsync(ct).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
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
    public async Task<int> GetLastSequenceAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return -1;

        var lastSequence = -1;
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
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope>(line, _readOptions);
                    if (envelope == null)
                        continue;
                    var sequence = envelope.Sequence ??
                        envelope.Payload.Deserialize<GameEvent>(_readOptions)?.Sequence ?? -1;
                    lastSequence = System.Math.Max(lastSequence, sequence);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to inspect event sequence: {ex.Message}", ex);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }

        return lastSequence;
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
                    gameEvent = gameEvent with
                    {
                        Context = gameEvent.Context.Merge(envelope.Context)
                    };

                    // Apply filters
                    if (filter.AfterSequence >= 0 && gameEvent.Sequence <= filter.AfterSequence)
                        continue;
                    if (filter.EventType != null &&
                        !string.Equals(gameEvent.EventType, filter.EventType, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (filter.RunId.HasValue &&
                        !MatchesGuid(gameEvent, gameEvent.Context.RunId, "runId", filter.RunId.Value))
                        continue;
                    if (filter.CombatId.HasValue &&
                        !MatchesGuid(gameEvent, gameEvent.Context.CombatId, "combatId", filter.CombatId.Value))
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

    private static bool MatchesGuid(
        GameEvent gameEvent,
        Guid? contextualValue,
        string payloadKey,
        Guid expected)
    {
        if (contextualValue.HasValue)
            return contextualValue.Value == expected;
        if (!gameEvent.Payload.TryGetValue(payloadKey, out var payloadValue))
            return false;

        return payloadValue switch
        {
            Guid guid => guid == expected,
            JsonElement element when element.ValueKind == JsonValueKind.String &&
                element.TryGetGuid(out var guid) => guid == expected,
            string text when Guid.TryParse(text, out var guid) => guid == expected,
            _ => false
        };
    }

    private sealed class EventEnvelope
    {
        public string EventType { get; set; } = string.Empty;
        public Guid EventId { get; set; }
        public DateTime Timestamp { get; set; }
        public int? Sequence { get; set; }
        public GameEventContext Context { get; set; } = GameEventContext.Empty;
        public JsonElement Payload { get; set; }
    }
}
