using System.Text.Json;
using Core.Abstractions.Persistence;
using Core.Determinism;
using Core.Run.Projections;

namespace Core.Run.Events;

public sealed record RunProjectionEvent
{
    public Guid EventId { get; init; }
    public int Sequence { get; init; }
    public int FactIndex { get; init; }
    public ulong Step { get; init; }
    public string EventType { get; init; } = RunCommitFacts.TransitionCommitted;
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
    public int? Round { get; init; }
    public int? Activation { get; init; }
    public JsonElement Payload { get; init; }
}

public readonly record struct RunEventCursor(int Sequence, int FactIndex)
{
    public static RunEventCursor AfterSequence(int sequence) => new(sequence, int.MaxValue);
    public override string ToString() => $"{Sequence}:{FactIndex}";
}

public interface IRunEventProjectionReader
{
    Task<IReadOnlyList<RunProjectionEvent>> ReadRunEventsAsync(
        Guid runId,
        RunEventCursor cursor,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunProjectionEvent>> ReadCombatEventsAsync(
        Guid runId,
        Guid combatId,
        RunEventCursor cursor,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Rebuildable durable events projected exclusively from facts in authoritative
/// run commits. Operational telemetry is deliberately outside this reader.
/// </summary>
public sealed class RunEventProjectionReader : IRunEventProjectionReader
{
    private readonly IRunCommitProjectionReader _commits;

    public RunEventProjectionReader(IRunCommitProjectionReader commits)
    {
        _commits = commits;
    }

    public async Task<IReadOnlyList<RunProjectionEvent>> ReadRunEventsAsync(
        Guid runId,
        RunEventCursor cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateCursor(cursor, limit);
        var commits = await _commits.ReadRunAsync(
            runId,
            CommitFloor(cursor),
            int.MaxValue,
            cancellationToken).ConfigureAwait(false);
        return commits.SelectMany(ToEvents)
            .Where(item => IsAfter(item, cursor))
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<RunProjectionEvent>> ReadCombatEventsAsync(
        Guid runId,
        Guid combatId,
        RunEventCursor cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateCursor(cursor, limit);
        var commits = await _commits.ReadCombatAsync(
            runId,
            combatId,
            CommitFloor(cursor),
            int.MaxValue,
            cancellationToken).ConfigureAwait(false);
        return commits
            .SelectMany(ToEvents)
            .Where(item => item.CombatId == combatId)
            .Where(item => IsAfter(item, cursor))
            .Take(limit)
            .ToArray();
    }

    private static IEnumerable<RunProjectionEvent> ToEvents(RunCommit commit) =>
        commit.Facts
            .OrderBy(fact => fact.FactIndex)
            .Select(fact => ToEvent(commit, fact));

    private static RunProjectionEvent ToEvent(RunCommit commit, RunCommitFact fact)
    {
        var entry = commit.ToJournalEntry();
        return new RunProjectionEvent
        {
            EventId = DeterministicId.Create(
                commit.RequireState().Determinism.Seed,
                checked((ulong)entry.Sequence),
                $"run-event:{fact.FactIndex}"),
            Sequence = entry.Sequence,
            FactIndex = fact.FactIndex,
            Step = fact.Step,
            EventType = fact.Type,
            CommandType = entry.CommandType,
            RunId = entry.RunId,
            CombatId = fact.CombatId,
            CommandId = entry.CommandId,
            CorrelationId = entry.CommandId ?? DeterministicId.Create(
                commit.RequireState().Determinism.Seed,
                checked((ulong)entry.Sequence),
                $"run-correlation:{entry.CommandType}"),
            Timestamp = entry.LogicalTimestamp,
            ConfigName = commit.RequireState().ConfigName,
            ContentRevision = commit.RequireState().Determinism.ContentRevision,
            Seed = commit.RequireState().Determinism.Seed,
            ExpectedSequence = entry.ExpectedSequence,
            ExpectedStep = entry.ExpectedStep,
            CommandPayloadHash = entry.CommandPayloadHash,
            PreviousStateHash = entry.PreviousStateHash,
            StateHash = entry.StateHash,
            Round = fact.Round,
            Activation = fact.Activation,
            Payload = fact.Payload.Clone()
        };
    }

    private static int CommitFloor(RunEventCursor cursor) =>
        cursor.FactIndex == int.MaxValue
            ? cursor.Sequence
            : System.Math.Max(0, cursor.Sequence - 1);

    private static bool IsAfter(RunProjectionEvent item, RunEventCursor cursor) =>
        item.Sequence > cursor.Sequence ||
        item.Sequence == cursor.Sequence && item.FactIndex > cursor.FactIndex;

    private static void ValidateCursor(RunEventCursor cursor, int limit)
    {
        if (cursor.Sequence < 0 || cursor.FactIndex < -1)
            throw new ArgumentOutOfRangeException(nameof(cursor));
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
