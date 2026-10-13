using System.Collections.Immutable;
using Core.Abstractions.Persistence;
using Core.Run.Branching;
using Core.Run.Dialogue;

namespace Core.Run.Replay;

public sealed record ReplayFrameHeader(int FrameIndex, ulong Step, string Scope, string Kind,
    Guid? CombatId, string? ActorId, string? PhaseId, int? Round, int? Activation);
public sealed record ReplayTimelineItem(int Sequence, ulong Step, Guid CommandId, string CommandType,
    DateTime LogicalTimestamp, string PreviousStateHash, string StateHash, int FrameCount,
    ImmutableArray<ReplayFrameHeader> Frames);
public sealed record ReplayTimelinePage(Guid RunId, string SettingId, RunLineage? Lineage,
    string ContentRevision, int AfterSequence, int ThroughSequence, int NextCursor, bool HasMore,
    int Returned, ImmutableArray<ReplayTimelineItem> Items, string ValidationKind = "StructuralChain",
    bool HistoricalInspectionAllowed = false);
public sealed record ReplayResource(string ResourceId, float Current, float Maximum);
public sealed record ReplayStateSummary(string Lifecycle, string? CurrentNodeId, int CardCount,
    int RelicCount, ImmutableArray<ReplayResource> Resources, ImmutableArray<DialogueTranscriptLine> Transcript);
public sealed record ReplayCommandPresentation(Guid RunId, string SettingId, RunLineage? Lineage,
    string ContentRevision, int Sequence, string CommandType, string StateHash, string PreviousStateHash,
    ReplayStateSummary StateSummary);

public interface IRunReplayQueryService
{
    Task<ReplayTimelinePage?> ReadTimelineAsync(Guid runId, int afterSequence, int limit,
        int? throughSequence = null, CancellationToken ct = default);
    Task<ReplayCommandPresentation?> ReadPresentationAsync(Guid runId, int sequence, CancellationToken ct = default);
}

/// <summary>Read models from the single journal. Navigation never materializes all historical states.</summary>
public sealed class RunReplayQueryService(IRunCommitEnvelopeReader envelopes, IRunCommitReader commits) : IRunReplayQueryService
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

    public async Task<ReplayCommandPresentation?> ReadPresentationAsync(Guid runId, int sequence, CancellationToken ct = default)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        var commit = await commits.LoadCommitAsync(runId, sequence, ct).ConfigureAwait(false);
        if (commit == null) return null;
        if (commit.RunId != runId || commit.Sequence != sequence)
            throw new InvalidDataException("Replay presentation does not match requested identity");
        var state = commit.RequireState();
        var summary = new ReplayStateSummary(state.Lifecycle.ToString(), state.CurrentNodeId,
            state.Deck.Topology.Instances.Count, state.Relics.Length,
            state.ResourceState.Resources.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ReplayResource(pair.Key, pair.Value.Current, pair.Value.Maximum)).ToImmutableArray(),
            state.Dialogues.Where(dialogue => dialogue.ActivityNodeId == state.CurrentNodeId)
                .SelectMany(dialogue => DialogueTransitions.RecordedTranscript(dialogue)).ToImmutableArray());
        return new(runId, state.SettingId, state.Lineage, state.Determinism.ContentRevision, sequence,
            commit.RootCommand.Type, commit.StateHash, commit.PreviousStateHash, summary);
    }
}
