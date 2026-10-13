using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Run.Branching;

namespace Core.Run.Replay;

public sealed record ReplayFrameHeader(int FrameIndex, ulong Step, string Scope, string Kind,
    Guid? CombatId, string? ActorId, string? PhaseId, int? Round, int? Activation);
public sealed record ReplayTimelineItem(int Sequence, ulong Step, Guid CommandId, string CommandType,
    DateTime LogicalTimestamp, string PreviousStateHash, string StateHash, int FrameCount,
    ImmutableArray<ReplayFrameHeader> Frames);
public sealed record ReplayTimelinePage(Guid RunId, string SettingId, RunLineage? Lineage,
    string ContentRevision, int AfterSequence, int ThroughSequence, int NextCursor, bool HasMore,
    int Returned, ImmutableArray<ReplayTimelineItem> Items, string ValidationKind = "StructuralChain");

public interface IRunReplayQueryService
{
    Task<ReplayTimelinePage?> ReadTimelineAsync(Guid runId, int afterSequence, int limit,
        int? throughSequence = null, CancellationToken ct = default);
}

/// <summary>Read models from the single journal. Navigation never materializes all historical states.</summary>
public sealed class RunReplayQueryService(IRunCommitEnvelopeReader envelopes) : IRunReplayQueryService
{
    public async Task<ReplayTimelinePage?> ReadTimelineAsync(Guid runId, int afterSequence, int limit,
        int? throughSequence = null, CancellationToken ct = default)
    {
        var page = await envelopes.LoadEnvelopePageAsync(runId, afterSequence, limit, throughSequence, ct).ConfigureAwait(false);
        if (page.InitialCommit == null) return null;
        var initial = page.InitialCommit.RequireState();
        var items = page.Items.Select(commit => new ReplayTimelineItem(commit.Sequence, commit.AfterStep,
            commit.RootCommand.CommandId, commit.RootCommand.Type, commit.LogicalTimestamp,
            commit.PreviousStateHash, commit.StateHash, commit.Frames.Count,
            commit.Frames.Select(frame => new ReplayFrameHeader(frame.FrameIndex, frame.Step, frame.Scope,
                frame.Kind, frame.CombatId, frame.ActorId, frame.PhaseId, frame.Round, frame.Activation)).ToImmutableArray())).ToImmutableArray();
        return new(runId, initial.SettingId, initial.Lineage, initial.Determinism.ContentRevision,
            afterSequence, page.ThroughSequence, page.NextCursor, page.HasMore, items.Length, items);
    }
}
