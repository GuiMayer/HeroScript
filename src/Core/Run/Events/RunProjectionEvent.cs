using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;

namespace Core.Run.Events;

public sealed record RunProjectionEvent
{
    public Guid EventId { get; init; }
    public int Sequence { get; init; }
    public ulong Step { get; init; }
    public string EventType { get; init; } = "RUN_TRANSITION_COMMITTED";
    public string CommandType { get; init; } = string.Empty;
    public Guid RunId { get; init; }
    public Guid? CombatId { get; init; }
    public Guid? CommandId { get; init; }
    public Guid CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UnixEpoch;
    public string ConfigName { get; init; } = string.Empty;
    public string ContentRevision { get; init; } = string.Empty;
    public ulong Seed { get; init; }
    public int? ExpectedSequence { get; init; }
    public ulong? ExpectedStep { get; init; }
    public string CommandPayloadHash { get; init; } = string.Empty;
    public string PreviousStateHash { get; init; } = string.Empty;
    public string StateHash { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
}

public interface IRunEventProjectionReader
{
    Task<IReadOnlyList<RunProjectionEvent>> ReadRunEventsAsync(
        Guid runId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunProjectionEvent>> ReadCombatEventsAsync(
        Guid runId,
        Guid combatId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Durable event projection derived from the append-only run journal. It is a
/// read model, not a second source of truth, so it can always be rebuilt.
/// </summary>
public sealed class RunEventProjectionReader : IRunEventProjectionReader
{
    private readonly IRunCommitStore _repository;

    public RunEventProjectionReader(IRunCommitStore repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<RunProjectionEvent>> ReadRunEventsAsync(
        Guid runId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateCursor(afterSequence, limit);
        var source = await _repository.LoadCommitsAsync(runId, cancellationToken).ConfigureAwait(false);
        return source
            .Where(item => item.Sequence > afterSequence)
            .OrderBy(item => item.Sequence)
            .Take(limit)
            .Select(item => ToEvent(item))
            .ToArray();
    }

    public async Task<IReadOnlyList<RunProjectionEvent>> ReadCombatEventsAsync(
        Guid runId,
        Guid combatId,
        int afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateCursor(afterSequence, limit);
        var source = await _repository.LoadCommitsAsync(runId, cancellationToken).ConfigureAwait(false);
        return source
            .Where(item => item.Sequence > afterSequence)
            .Where(item => item.StateAfter.GetEncounter(combatId) != null)
            .Where(item => item.Frames.Any(frame => frame.CombatId == combatId))
            .OrderBy(item => item.Sequence)
            .Take(limit)
            .Select(item => ToEvent(item, combatId))
            .ToArray();
    }

    private static RunProjectionEvent ToEvent(RunCommit commit, Guid? combatId = null)
    {
        var entry = commit.ToJournalEntry();
        var resolvedCombatId = combatId ?? ResolveCombatId(commit);
        return new RunProjectionEvent
        {
            EventId = DeterministicId.Create(
                commit.StateAfter.Determinism.Seed,
                checked((ulong)entry.Sequence),
                $"run-event:{entry.CommandType}"),
            Sequence = entry.Sequence,
            Step = entry.Step,
            EventType = "RUN_TRANSITION_COMMITTED",
            CommandType = entry.CommandType,
            RunId = entry.RunId,
            CombatId = resolvedCombatId,
            CommandId = entry.CommandId,
            CorrelationId = entry.CommandId ?? DeterministicId.Create(
                commit.StateAfter.Determinism.Seed,
                checked((ulong)entry.Sequence),
                $"run-correlation:{entry.CommandType}"),
            Timestamp = entry.LogicalTimestamp,
            ConfigName = commit.StateAfter.ConfigName,
            ContentRevision = commit.StateAfter.Determinism.ContentRevision,
            Seed = commit.StateAfter.Determinism.Seed,
            ExpectedSequence = entry.ExpectedSequence,
            ExpectedStep = entry.ExpectedStep,
            CommandPayloadHash = entry.CommandPayloadHash,
            PreviousStateHash = entry.PreviousStateHash,
            StateHash = entry.StateHash,
            Payload = entry.Command.Clone()
        };
    }

    private static Guid? ResolveCombatId(RunCommit commit)
    {
        var scoped = commit.Frames.Select(frame => frame.CombatId).FirstOrDefault(id => id.HasValue);
        if (scoped.HasValue)
            return scoped;
        var entry = commit.ToJournalEntry();
        if (entry.Command.ValueKind == JsonValueKind.Object &&
            entry.Command.TryGetProperty("combatId", out var combatId) &&
            combatId.ValueKind == JsonValueKind.String &&
            combatId.TryGetGuid(out var parsed))
        {
            return parsed;
        }

        return commit.StateAfter.ActiveEncounterId ??
               commit.StateAfter.Encounters.LastOrDefault()?.Combat.CombatId;
    }

    private static void ValidateCursor(int afterSequence, int limit)
    {
        if (afterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
